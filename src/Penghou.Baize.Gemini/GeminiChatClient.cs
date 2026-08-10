using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// <see cref="ILlmClient"/> implementation for the Gemini streaming API
/// (<c>POST /v1beta/models/{model}:streamGenerateContent?alt=sse</c>). Streams
/// text and function-call deltas to the canonical event stream, surfaces
/// <c>thought</c> parts as reasoning, and maps finish reasons and usage into
/// the provider-neutral shape.
/// </summary>
public sealed class GeminiChatClient : LlmClientBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly Uri _chatUri;

    /// <summary>
    /// Creates a Gemini streaming client.
    /// </summary>
    /// <param name="model">The Gemini model identifier (for example <c>gemini-2.5-flash</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The Gemini API key.</param>
    /// <param name="baseUrl">Base API URL. When it does not already include a version segment such as <c>v1beta</c> or <c>v1</c>, <c>v1beta</c> is appended.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    public GeminiChatClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities)
        : base(model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var normalizedBaseUrl =
            baseUrl.TrimEnd('/');
        var lastSegment =
            normalizedBaseUrl[
                (normalizedBaseUrl.LastIndexOf('/') + 1)..];
        var includeVersionSegment =
            !LooksLikeApiVersion(lastSegment);

        _chatUri = new Uri(
            $"{normalizedBaseUrl}" +
            $"{(includeVersionSegment ? "/v1beta" : string.Empty)}" +
            $"/models/{model}:streamGenerateContent?alt=sse");
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
    {
        var wireRequest = ToWireRequest(request);
        var json = JsonSerializer.Serialize(
            wireRequest,
            JsonOptions);

        var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                _chatUri);

        httpRequest.Headers.Add(
            "x-goog-api-key",
            ApiKey);

        httpRequest.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        return httpRequest;
    }

    /// <inheritdoc />
    protected override void ValidateRequest(LlmRequest request) =>
        GeminiMessageRequestMapper.Validate(Model, Capabilities, request);

    /// <inheritdoc />
    private GeminiChatRequest ToWireRequest(LlmRequest request) =>
        GeminiMessageRequestMapper.Build(Model, Capabilities, request);

    private static bool LooksLikeApiVersion(string segment) =>
        segment.Length >= 2 &&
        segment[0] == 'v' &&
        char.IsDigit(segment[1]);

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var receivedChunk = false;
        var receivedFinalChunk = false;
        var contentLength = 0;
        var nativeToolCallCount = 0;
        var nextPartIndex = 0;

        await foreach (var (_, data) in ReadSseEventsAsync(stream, cancellationToken))
        {
            // Gemini terminates the stream with a final data: [DONE] event.
            if (data == "[DONE]")
            {
                receivedFinalChunk = true;
                break;
            }

            receivedChunk = true;

            GeminiChatResponse? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<GeminiChatResponse>(
                    data,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Gemini stream JSON parse error: {ex.Message}",
                    ex);
            }

            if (chunk is null)
                continue;

            if (chunk.Candidates is null || chunk.Candidates.Count == 0)
                continue;

            var candidate = chunk.Candidates[0];

            if (candidate.Content is null)
                continue;

            var content = candidate.Content;

            if (content.Parts is null)
                continue;

            foreach (var part in content.Parts)
            {
                var partIndex = nextPartIndex++;
                var continuation = part.ThoughtSignature is null
                    ? null
                    : new LlmProviderContinuation(
                        Provider: "Gemini",
                        Values: new Dictionary<string, string>
                        {
                            ["thoughtSignature"] =
                                part.ThoughtSignature
                        });

                if (part.Text is not null)
                {
                    contentLength += part.Text.Length;

                    if (part.Thought == true)
                    {
                        yield return new LlmStreamEvent(
                            ReasoningContent: part.Text,
                            Continuation: continuation)
                        {
                            PartIndex = partIndex
                        };
                    }
                    else
                    {
                        yield return new LlmStreamEvent(
                            Delta: part.Text,
                            Continuation: continuation)
                        {
                            PartIndex = partIndex
                        };
                    }
                }

                if (part.FunctionCall is not null)
                {
                    var functionCall = part.FunctionCall;

                    yield return new LlmStreamEvent(
                        ToolCallDelta: new ToolCallDelta(
                            Index: nativeToolCallCount,
                            Id: functionCall.Id ??
                                Guid.NewGuid().ToString("N"),
                            Name: functionCall.Name,
                            ArgumentsJsonFragment:
                                functionCall.Args.ToString(),
                            Continuation: continuation),
                        Continuation: continuation)
                    {
                        PartIndex = partIndex
                    };

                    nativeToolCallCount++;
                }
            }

            if (candidate.FinishReason is not null)
            {
                receivedFinalChunk = true;

                yield return new LlmStreamEvent(
                    FinishReason:
                        MapFinishReason(
                            candidate.FinishReason));
            }

            if (chunk.Usage is not null)
            {
                yield return new LlmStreamEvent(
                    Usage: new LlmUsage(
                        PromptTokens:
                            chunk.Usage.PromptTokenCount,
                        CompletionTokens:
                            chunk.Usage.CandidatesTokenCount,
                        TotalTokens:
                            chunk.Usage.TotalTokenCount));
            }
        }

        if (!receivedChunk)
            throw new LlmClientException(
                "Gemini stream returned no chunks.",
                LlmClientFailureKind.Availability);

        // A complete Gemini response must report a finish reason on its final
        // candidate (or be terminated by the [DONE] sentinel). A stream that
        // emitted partial content but ended without one is truncated and must
        // be surfaced as an availability failure so the router can fail over,
        // rather than accepted as a "successful" partial answer.
        if (!receivedFinalChunk)
            throw new LlmClientException(
                "Gemini stream ended without a final chunk.",
                LlmClientFailureKind.Availability);
    }

    private static string MapFinishReason(string finishReason) =>
        finishReason switch
        {
            "STOP" => "stop",
            "MAX_OUTPUT_TOKENS" => "length",
            "SAFETY" => "content_filter",
            _ => finishReason.ToLowerInvariant()
        };
}
