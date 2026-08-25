using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// <see cref="ILlmClient"/> implementation for the OpenAI Chat Completions
/// streaming API (<c>POST /chat/completions</c>). Streams content,
/// <c>reasoning_content</c> and tool-call deltas to the canonical event
/// stream, and forwards usage from the streamed usage chunk.
/// </summary>
public sealed class OpenAiChatClient : LlmClientBase
{
    private readonly Uri _chatCompletionsUri;
    private readonly OpenAiDialect _dialect;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy =
                JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    /// <summary>
    /// Creates an OpenAI Chat Completions client.
    /// </summary>
    /// <param name="model">The model identifier (for example <c>gpt-4o-mini</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="baseUrl">Base API URL, for example <c>https://api.openai.com/v1</c>; must end in <c>/v1</c> or the completions path is appended directly.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="dialect">The OpenAI-compatible wire dialect of the endpoint.</param>
    public OpenAiChatClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect = OpenAiDialect.Standard)
        : base(model, httpClientFactory, apiKey, OpenAiDialectPolicy.Apply(capabilities, dialect), "OpenAi")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _dialect = dialect;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _chatCompletionsUri = new Uri($"{normalizedBaseUrl}/chat/completions");
    }

    /// <summary>Applies the OpenAI bearer scheme.</summary>
    protected override void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrEmpty(ApiKey))
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", ApiKey);
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
    {
        var wireRequest = ToWireRequest(request);
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _chatCompletionsUri);

        var json = JsonSerializer.Serialize(wireRequest, JsonOptions);

        httpRequest.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        return httpRequest;
    }

    /// <summary>Maps the neutral request onto the OpenAI wire format.</summary>
    private OpenAiChatCompletionRequest ToWireRequest(LlmRequest request) =>
        OpenAiChatCompletionRequestMapper.Build(
            Model,
            Capabilities,
            _dialect,
            request,
            streaming: true);

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var receivedTerminal = false;
        var nextPartIndex = 0;
        int? reasoningPartIndex = null;
        int? contentPartIndex = null;
        var toolPartIndices = new Dictionary<int, int>();
        var syntheticToolIndices = new HashSet<int>();
        string? responseId = null;
        string? actualModel = null;
        string? serviceTier = null;
        string? systemFingerprint = null;
        var nativeToolCallCount = 0;

        await foreach (var (_, data) in ReadSseEventsAsync(stream, cancellationToken))
        {
            // OpenAI terminates a complete stream with a final data: [DONE]
            // event. A truncation (connection drop before [DONE]) leaves the
            // stream without this signal, so the caller can reject it as
            // incomplete rather than surfacing partial code.
            if (data == "[DONE]")
            {
                receivedTerminal = true;
                break;
            }

            OpenAiChatCompletionChunk? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<OpenAiChatCompletionChunk>(
                    data,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Failed to parse streaming chunk: {data}",
                    ex);
            }

            if (chunk is null)
                continue;

            responseId = chunk.Id ?? responseId;
            actualModel = chunk.Model ?? actualModel;
            serviceTier = chunk.ServiceTier ?? serviceTier;
            systemFingerprint = chunk.SystemFingerprint ?? systemFingerprint;

            if (chunk.Usage is not null)
            {
                yield return new LlmStreamEvent(
                    Delta: null,
                    FinishReason: null,
                    Usage: new LlmUsage(
                        PromptTokens: chunk.Usage.PromptTokens,
                        CompletionTokens: chunk.Usage.CompletionTokens,
                        TotalTokens: chunk.Usage.TotalTokens,
                        PromptCacheHitTokens: chunk.Usage.PromptCacheHitTokens,
                        PromptCacheMissTokens: chunk.Usage.PromptCacheMissTokens));
            }

            // Baize never requests multiple chat choices (no "n" parameter is
            // sent), so the first choice is the whole response by contract.
            var choice = chunk.Choices?.FirstOrDefault();

            if (choice is null)
                continue;

            var delta = choice.Delta?.Content;
            var finishReason = choice.FinishReason;

            // A finish reason also marks the turn complete, so a stream that
            // reported one (and then ended) is not treated as truncated even if
            // the trailing [DONE] was not observed.
            if (finishReason is not null)
                receivedTerminal = true;

            if (!string.IsNullOrEmpty(choice.Delta?.ReasoningContent))
            {
                reasoningPartIndex ??= nextPartIndex++;

                yield return new LlmStreamEvent(
                    ReasoningContent: choice.Delta.ReasoningContent,
                    Continuation: new LlmProviderContinuation(
                        Provider: "OpenAi",
                        Values: new Dictionary<string, string>()))
                {
                    PartIndex = reasoningPartIndex
                };
            }

            if (!string.IsNullOrEmpty(delta) || finishReason is not null)
            {
                if (!string.IsNullOrEmpty(delta))
                    contentPartIndex ??= nextPartIndex++;

                yield return new LlmStreamEvent(
                    Delta: delta,
                    FinishReason: finishReason,
                    Usage: null)
                {
                    PartIndex = delta is null ? null : contentPartIndex
                };
            }

            yield return new LlmStreamEvent(
                Diagnostics: new LlmProviderDiagnostics(
                    Provider: "OpenAi",
                    ActualModel: actualModel,
                    Api: "native",
                    Done: receivedTerminal,
                    NativeToolCallCount: nativeToolCallCount,
                    ResponseId: responseId,
                    ServiceTier: serviceTier,
                    SystemFingerprint: systemFingerprint));

            if (choice.Delta?.ToolCalls is { Count: > 0 } toolCallDeltas)
            {
                foreach (var toolCallDelta in toolCallDeltas)
                {
                    if (string.Equals(
                            toolCallDelta.Function?.Name,
                            OpenAiStructuredOutput.ToolName,
                            StringComparison.Ordinal))
                    {
                        syntheticToolIndices.Add(toolCallDelta.Index);
                    }

                    if (syntheticToolIndices.Contains(toolCallDelta.Index))
                    {
                        if (!string.IsNullOrEmpty(toolCallDelta.Function?.Arguments))
                        {
                            contentPartIndex ??= nextPartIndex++;
                            yield return new LlmStreamEvent(
                                Delta: toolCallDelta.Function.Arguments)
                            {
                                PartIndex = contentPartIndex
                            };
                        }

                        continue;
                    }

                    if (!toolPartIndices.TryGetValue(
                            toolCallDelta.Index,
                            out var toolPartIndex))
                    {
                        toolPartIndex = nextPartIndex++;
                        toolPartIndices[toolCallDelta.Index] = toolPartIndex;
                        nativeToolCallCount++;
                    }

                    yield return new LlmStreamEvent(
                        ToolCallDelta: new ToolCallDelta(
                            Index: toolCallDelta.Index,
                            Id: toolCallDelta.Id,
                            Name: toolCallDelta.Function?.Name,
                            ArgumentsJsonFragment: toolCallDelta.Function?.Arguments))
                    {
                        PartIndex = toolPartIndex
                    };
                }
            }
        }

        if (!receivedTerminal)
            throw new LlmClientException(
                "OpenAI streaming response ended without a terminal chunk.",
                LlmClientFailureKind.Availability);
    }
}
