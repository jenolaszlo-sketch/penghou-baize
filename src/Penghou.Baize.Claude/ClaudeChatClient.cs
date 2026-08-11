using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Claude;

/// <summary>
/// <see cref="ILlmClient"/> implementation for the Anthropic Claude Messages
/// API (<c>POST /v1/messages</c>). Streams text and tool-call deltas to the
/// canonical event stream, surfaces <c>thinking_delta</c> reasoning, combines
/// input usage from <c>message_start</c> with output usage from
/// <c>message_delta</c>, and repackages the synthetic <c>structured_output</c>
/// tool (used to emulate JSON-schema responses) as plain content so it never
/// leaks as a tool call.
/// </summary>
public sealed class ClaudeChatClient : LlmClientBase
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly Uri _messagesUri;
    private readonly ClaudeThinkingStyle _thinkingStyle;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a Claude Messages client.
    /// </summary>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="model">The Anthropic model identifier (for example <c>claude-sonnet-4-5</c>).</param>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="baseUrl">Base API URL; defaults to <c>https://api.anthropic.com</c>.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="thinkingStyle">The extended-thinking contract the model generation uses.</param>
    public ClaudeChatClient(
        IHttpClientFactory httpClientFactory,
        string model,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities,
        ClaudeThinkingStyle thinkingStyle = ClaudeThinkingStyle.Adaptive)
        : base(model, httpClientFactory, apiKey, capabilities, "Claude")
    {
        _thinkingStyle = thinkingStyle;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _messagesUri = new Uri($"{normalizedBaseUrl}/v1/messages");
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
    {
        var wireRequest = ToWireRequest(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _messagesUri);

        httpRequest.Headers.Add("x-api-key", ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        var json = JsonSerializer.Serialize(wireRequest, JsonOptions);

        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return httpRequest;
    }

    /// <inheritdoc />
    private ClaudeMessageRequest ToWireRequest(LlmRequest request) =>
        ClaudeMessageRequestMapper.Build(
            Model,
            Capabilities,
            _thinkingStyle,
            request,
            streaming: true);

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ClaudeUsage? inputUsage = null;
        var syntheticToolJson = new Dictionary<int, StringBuilder>();
        var receivedMessageStop = false;

        await foreach (var (eventType, data) in ReadSseEventsAsync(stream, cancellationToken))
        {
            switch (eventType)
            {
                case "message_start":
                    {
                        var msgStart = TryDeserialize<ClaudeMessageStart>(data);
                        inputUsage = msgStart?.Message?.Usage;
                        break;
                    }

                case "content_block_delta":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);
                        var delta = evt?.Delta;
                        var text = delta?.Text;

                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new LlmStreamEvent(Delta: text)
                            {
                                PartIndex = evt?.Index
                            };
                        }

                        if (delta?.Type == "thinking_delta" &&
                            !string.IsNullOrEmpty(delta.Thinking))
                        {
                            yield return new LlmStreamEvent(
                                ReasoningContent: delta.Thinking)
                            {
                                PartIndex = evt?.Index
                            };
                        }

                        // Claude sends the thinking block's signature in its own
                        // delta, after the thinking text. Surface it as a bare
                        // continuation so the collector ties it to the reasoning
                        // that preceded it and can replay it with the text.
                        if (delta?.Type == "signature_delta" &&
                            !string.IsNullOrEmpty(delta.Signature))
                        {
                            yield return new LlmStreamEvent(
                                Continuation:
                                    new LlmProviderContinuation(
                                        Provider: "Claude",
                                        Values: new Dictionary<string, string>
                                        {
                                            ["signature"] =
                                                delta.Signature
                                        }))
                            {
                                PartIndex = evt?.Index
                            };
                        }

                        if (delta?.Type == "input_json_delta" &&
                            delta.PartialJson is not null &&
                            evt?.Index is { } toolIndex)
                        {
                            if (syntheticToolJson.TryGetValue(toolIndex, out var builder))
                            {
                                builder.Append(delta.PartialJson);
                            }
                            else
                            {
                                yield return new LlmStreamEvent(
                                    ToolCallDelta: new ToolCallDelta(
                                        Index: toolIndex,
                                        ArgumentsJsonFragment: delta.PartialJson))
                                {
                                    PartIndex = toolIndex
                                };
                            }
                        }

                        break;
                    }

                case "content_block_start":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);
                        var block = evt?.ContentBlock;

                        if (block?.Type == "thinking" && evt?.Index is { } thinkingIndex)
                        {
                            var continuation = string.IsNullOrEmpty(block.Signature)
                                ? null
                                : new LlmProviderContinuation(
                                    Provider: "Claude",
                                    Values: new Dictionary<string, string>
                                    {
                                        ["signature"] = block.Signature
                                    });

                            yield return new LlmStreamEvent(
                                ReasoningContent: block.Thinking ?? string.Empty,
                                Continuation: continuation)
                            {
                                PartIndex = thinkingIndex
                            };
                        }

                        if (block?.Type == "redacted_thinking" &&
                            block.Data is { } redactedData &&
                            evt?.Index is { } redactedIndex)
                        {
                            yield return new LlmStreamEvent(
                                ReasoningContent: string.Empty,
                                Continuation: new LlmProviderContinuation(
                                    Provider: "Claude",
                                    Values: new Dictionary<string, string>
                                    {
                                        ["redactedThinkingData"] = redactedData
                                    }))
                            {
                                PartIndex = redactedIndex
                            };
                        }

                        if (block?.Type == "tool_use" &&
                            evt?.Index is { } toolIndex)
                        {
                            if (block.Name == ClaudeMessageRequestMapper.StructuredOutputToolName)
                            {
                                syntheticToolJson[toolIndex] = new StringBuilder();
                            }
                            else
                            {
                                yield return new LlmStreamEvent(
                                    ToolCallDelta: new ToolCallDelta(
                                        Index: toolIndex,
                                        Id: block.Id,
                                        Name: block.Name))
                                {
                                    PartIndex = toolIndex
                                };
                            }
                        }

                        break;
                    }

                case "content_block_stop":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);

                        if (evt?.Index is { } toolIndex &&
                            syntheticToolJson.Remove(toolIndex, out var builder) &&
                            builder.Length > 0)
                        {
                            // The structured_output tool is a Claude
                            // workaround for missing native JSON schema
                            // support. Surface its payload as content instead
                            // of leaking it as a tool call.
                            yield return new LlmStreamEvent(
                                Delta: builder.ToString())
                            {
                                PartIndex = toolIndex
                            };
                        }

                        break;
                    }

                case "message_delta":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);

                        var stopReason = evt?.Delta?.StopReason;
                        var outputTokens = evt?.Usage?.OutputTokens;

                        var usage = (inputUsage is not null || outputTokens is not null)
                            ? new LlmUsage(
                                PromptTokens: inputUsage?.InputTokens,
                                CompletionTokens: outputTokens,
                                TotalTokens: (inputUsage?.InputTokens ?? 0) + (outputTokens ?? 0),
                                PromptCacheHitTokens: inputUsage?.CacheReadInputTokens,
                                PromptCacheMissTokens: inputUsage?.CacheCreationInputTokens)
                            : null;

                        yield return new LlmStreamEvent(
                            FinishReason: stopReason,
                            Usage: usage);

                        break;
                    }

                case "message_stop":
                    receivedMessageStop = true;
                    yield break;

                case "error":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);
                        var errorType = evt?.Error?.Type ?? "unknown_error";
                        var errorMessage = evt?.Error?.Message ?? data;
                        var retryAfterSeconds = TryReadRetryAfterSeconds(data);

                        throw new LlmClientException(
                            $"Claude streaming error ({errorType}): {errorMessage}",
                            ClaudeErrorClassifier.ClassifyFailureKind(errorType),
                            statusCode: null,
                            rateLimit: retryAfterSeconds is { } seconds
                                ? new LlmRateLimitInfo(
                                    RetryAfter: TimeSpan.FromSeconds(seconds))
                                : null);
                    }
            }
        }

        if (!receivedMessageStop)
            throw new LlmClientException(
                "Claude streaming response ended without a message_stop event.",
                LlmClientFailureKind.Availability);
    }

    /// <inheritdoc />
    protected override void ValidateRequest(LlmRequest request) =>
        ClaudeMessageRequestMapper.Validate(Model, Capabilities, request);

    private static int? TryReadRetryAfterSeconds(
        string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);

            if (document.RootElement.TryGetProperty(
                    "error",
                    out var error) &&
                error.TryGetProperty(
                    "retry_after",
                    out var retryAfter) &&
                retryAfter.ValueKind == JsonValueKind.Number)
            {
                return retryAfter.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static T? TryDeserialize<T>(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, JsonOptions);
        }
        catch (JsonException ex)
        {
            // A malformed stream event indicates transport corruption or API
            // drift; surface it as an error rather than silently dropping the
            // event and turning it into a confusing failure later.
            throw new LlmClientException(
                $"Failed to parse Claude streaming event: {data}",
                ex);
        }
    }
}
