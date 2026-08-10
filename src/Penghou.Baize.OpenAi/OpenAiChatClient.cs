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
public sealed class OpenAiChatClient : LlmClientBase<OpenAiChatCompletionRequest>
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
        : base(model, httpClientFactory, apiKey, BoostForDialect(capabilities, dialect))
    {
        _dialect = dialect;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _chatCompletionsUri = new Uri($"{normalizedBaseUrl}/chat/completions");
    }

    /// <summary>
    /// The OpenAI-compatible <c>thinking</c> toggle is only valid on DeepSeek
    /// models (real OpenAI rejects the unknown parameter), so the endpoint's
    /// <c>ThinkingDisable</c> capability is derived from the declared dialect:
    /// true for <see cref="OpenAiDialect.DeepSeek"/>, false otherwise. Because
    /// the conservative style defaults do not claim extended thinking, the
    /// DeepSeek dialect also enables <see cref="LlmEndpointCapabilities.Thinking"/>:
    /// declaring the dialect is itself an explicit opt-in. This keeps request
    /// validation honest regardless of how the caller populated the flags.
    /// </summary>
    private static LlmEndpointCapabilities BoostForDialect(
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect) =>
        dialect == OpenAiDialect.DeepSeek
            ? capabilities with { Thinking = true, ThinkingDisable = true }
            : capabilities with { ThinkingDisable = false };

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(OpenAiChatCompletionRequest wireRequest)
    {
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _chatCompletionsUri);

        if (!string.IsNullOrEmpty(ApiKey))
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", ApiKey);

        var json = JsonSerializer.Serialize(wireRequest, JsonOptions);

        httpRequest.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        return httpRequest;
    }

    /// <inheritdoc />
    protected override OpenAiChatCompletionRequest ToWireRequest(LlmRequest request)
    {
        return new OpenAiChatCompletionRequest
        {
            Model = Model,
            Messages = request.Messages
                .SelectMany(ToWireMessages)
                .ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = true,
            StreamOptions = new OpenAiStreamOptions { IncludeUsage = true },
            Tools = !Capabilities.NativeToolCalling
                ? null
                : request.Tools?.Select(t => new OpenAiTool
                {
                    Function = new OpenAiFunctionTool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = ParseJsonElement(
                            t.InputSchemaJson,
                            $"tool schema '{t.Name}'")
                    }
                }).ToList(),
            ResponseFormat = request.ResponseFormat is null
                ? null
                : new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "response",
                        schema = ParseJsonElement(
                            request.ResponseFormat.Schema,
                            "response format schema"),
                        strict = true
                    }
                },
            ReasoningEffort = request.ThinkingConfig is null || request.ThinkingConfig.Mode != LlmThinkingMode.Enabled
                ? null
                : MapThinkingEffort(request.ThinkingConfig.Effort),
            Thinking = MapThinkingToggle(request.ThinkingConfig)
        };
    }

    private object? MapThinkingToggle(LlmThinkingConfig? config)
    {
        if (config is null || config.Mode == LlmThinkingMode.ProviderDefault)
        {
            return null;
        }

        if (_dialect != OpenAiDialect.DeepSeek)
        {
            return null;
        }

        return new
        {
            type = config.Mode == LlmThinkingMode.Enabled
                ? "enabled"
                : "disabled"
        };
    }

    private IEnumerable<OpenAiChatMessage> ToWireMessages(LlmMessage message)
    {
        var text = string.Concat(
            message.Parts
                .OfType<LlmTextContent>()
                .Select(part => part.Text));
        var reasoning = _dialect == OpenAiDialect.DeepSeek
            ? string.Concat(
                message.Parts
                    .OfType<LlmReasoningContent>()
                    .Where(part =>
                        part.Continuation is null ||
                        part.Continuation.IsFor("OpenAi"))
                    .Select(part => part.Text))
            : string.Empty;
        var toolCalls = message.Parts
            .OfType<LlmToolCallContent>()
            .Select(part => part.ToolCall)
            .ToList();

        if (toolCalls.Count > 0)
        {
            yield return new OpenAiChatMessage
            {
                Role = message.Role,
                Content = string.IsNullOrEmpty(text) ? null : text,
                ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
                ToolCalls = toolCalls
                    .Select(call => new OpenAiToolCall
                    {
                        Id = call.Id,
                        Type = "function",
                        Function = new OpenAiToolCallFunction
                        {
                            Name = call.Name,
                            Arguments = call.ArgumentsJson
                        }
                    })
                    .ToList()
            };

            yield break;
        }

        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var result in message.Parts
                .OfType<LlmToolResultContent>()
                .Select(part => part.Result))
            {
                yield return new OpenAiChatMessage
                {
                    Role = "tool",
                    ToolCallId = result.ToolCallId,
                    Content = result.Content
                };
            }

            yield break;
        }

        yield return new OpenAiChatMessage
        {
            Role = message.Role,
            Content = string.IsNullOrEmpty(text) ? null : text,
            ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning
        };
    }

    private static string? MapThinkingEffort(LlmThinkingEffort effort) =>
        effort switch
        {
            LlmThinkingEffort.None => null,
            LlmThinkingEffort.Low => "low",
            LlmThinkingEffort.Medium => "medium",
            LlmThinkingEffort.High => "high",
            // OpenAI has no "max" reasoning effort on the wire; reject rather
            // than silently capping to "high".
            LlmThinkingEffort.Max => throw new LlmRequestValidationException(
                "OpenAI does not support a 'max' reasoning effort; it would " +
                "be silently capped to 'high'."),
            _ => null
        };

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

            if (choice.Delta?.ToolCalls is { Count: > 0 } toolCallDeltas)
            {
                foreach (var toolCallDelta in toolCallDeltas)
                {
                    if (!toolPartIndices.TryGetValue(
                            toolCallDelta.Index,
                            out var toolPartIndex))
                    {
                        toolPartIndex = nextPartIndex++;
                        toolPartIndices[toolCallDelta.Index] = toolPartIndex;
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
