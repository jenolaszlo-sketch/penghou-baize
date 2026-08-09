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
public sealed class ClaudeChatClient : LlmClientBase<ClaudeMessageRequest>
{
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 4096;
    private const string StructuredOutputToolName = "structured_output";

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
        : base(model, httpClientFactory, apiKey, capabilities)
    {
        _thinkingStyle = thinkingStyle;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _messagesUri = new Uri($"{normalizedBaseUrl}/v1/messages");
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(ClaudeMessageRequest wireRequest)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _messagesUri);

        httpRequest.Headers.Add("x-api-key", ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        var json = JsonSerializer.Serialize(wireRequest, JsonOptions);

        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return httpRequest;
    }

    /// <inheritdoc />
    protected override ClaudeMessageRequest ToWireRequest(LlmRequest request)
    {
        var system = string.Join(
            Environment.NewLine + Environment.NewLine,
            request.Messages
                .Where(m => m.Role.Equals(
                    "system",
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(m => m.Parts.OfType<LlmTextContent>())
                .Select(part => part.Text));

        var nonSystemMessages = request.Messages
            .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .SelectMany(ToWireMessages)
            .ToList();

        return new ClaudeMessageRequest
        {
            Model = Model,
            System = string.IsNullOrEmpty(system)
                ? null
                : system,
            Messages = nonSystemMessages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens ?? DefaultMaxTokens,
            Stream = true,
            Tools = ToWireTools(request),
            Thinking = MapThinking(request.ThinkingConfig),
            OutputConfig = MapOutputConfig(request.ThinkingConfig)
        };
    }

    /// <summary>
    /// Emits the <c>thinking</c> block when thinking is enabled. Adaptive
    /// models receive <c>{"type":"adaptive"}</c>; manual-thinking models
    /// receive <c>{"type":"enabled","budget_tokens":N}</c> where the budget
    /// comes from <see cref="LlmEndpointCapabilities.ThinkingBudget"/> or is
    /// derived from the requested effort.
    /// </summary>
    private ClaudeThinking? MapThinking(LlmThinkingConfig? config)
    {
        if (config is null || config.Mode != LlmThinkingMode.Enabled)
        {
            return null;
        }

        return _thinkingStyle == ClaudeThinkingStyle.Manual
            ? MapThinkingBudget(config.Effort) is { } budget
                ? new ClaudeThinking
                {
                    Type = "enabled",
                    BudgetTokens = budget
                }
                : null
            : new ClaudeThinking { Type = "adaptive" };
    }

    /// <summary>
    /// Emits the <c>output_config</c> block for adaptive thinking. Manual
    /// thinking is controlled by the token budget instead, so effort is not
    /// expressed there.
    /// </summary>
    private ClaudeOutputConfig? MapOutputConfig(LlmThinkingConfig? config)
    {
        if (config is null ||
            config.Mode != LlmThinkingMode.Enabled ||
            _thinkingStyle == ClaudeThinkingStyle.Manual)
        {
            return null;
        }

        return MapThinkingEffort(config.Effort) is { } effort
            ? new ClaudeOutputConfig { Effort = effort }
            : null;
    }

    private int? MapThinkingBudget(LlmThinkingEffort effort) =>
        Capabilities.ThinkingBudget ?? EffortToBudget(effort);

    private static int? EffortToBudget(LlmThinkingEffort effort) =>
        effort switch
        {
            LlmThinkingEffort.Low => 1024,
            LlmThinkingEffort.Medium => 4096,
            LlmThinkingEffort.High => 8192,
            // Claude has no explicit "max" tier; use the largest documented
            // budget. Prefer an explicit Capabilities.ThinkingBudget to match
            // the exact model range.
            LlmThinkingEffort.Max => 16000,
            _ => null
        };

    private IEnumerable<ClaudeMessage> ToWireMessages(LlmMessage message)
    {
        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var result in message.Parts
                .OfType<LlmToolResultContent>()
                .Select(part => part.Result))
            {
                yield return new ClaudeMessage
                {
                    Role = "user",
                    Content =
                    [
                        new ClaudeContentBlock
                        {
                            Type = "tool_result",
                            ToolUseId = result.ToolCallId,
                            Content = result.Content,
                            IsError = !result.Succeeded
                        }
                    ]
                };
            }

            yield break;
        }

        var blocks = new List<ClaudeContentBlock>();

        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case LlmTextContent text:
                    blocks.Add(new ClaudeContentBlock
                    {
                        Type = "text",
                        Text = text.Text
                    });
                    break;

                case LlmToolCallContent toolCall:
                    blocks.Add(new ClaudeContentBlock
                    {
                        Type = "tool_use",
                        Id = toolCall.ToolCall.Id,
                        Name = toolCall.ToolCall.Name,
                        Input = ParseJsonElement(
                            toolCall.ToolCall.ArgumentsJson,
                            $"tool call '{toolCall.ToolCall.Name}' arguments")
                    });
                    break;

                // Reasoning parts are dropped: Claude cannot accept thinking
                // blocks back without the provider's exact signatures.
            }
        }

        if (blocks.Count == 0)
            yield break;

        yield return new ClaudeMessage
        {
            Role = message.Role,
            Content = blocks
        };
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ClaudeUsage? inputUsage = null;
        var syntheticToolJson = new Dictionary<int, StringBuilder>();

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
                            yield return new LlmStreamEvent(Delta: text);

                        if (delta?.Type == "thinking_delta" &&
                            !string.IsNullOrEmpty(delta.Thinking))
                        {
                            yield return new LlmStreamEvent(
                                ReasoningContent: delta.Thinking);
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
                                        ArgumentsJsonFragment: delta.PartialJson));
                            }
                        }

                        break;
                    }

                case "content_block_start":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);
                        var block = evt?.ContentBlock;

                        if (block?.Type == "tool_use" &&
                            evt?.Index is { } toolIndex)
                        {
                            if (block.Name == StructuredOutputToolName)
                            {
                                syntheticToolJson[toolIndex] = new StringBuilder();
                            }
                            else
                            {
                                yield return new LlmStreamEvent(
                                    ToolCallDelta: new ToolCallDelta(
                                        Index: toolIndex,
                                        Id: block.Id,
                                        Name: block.Name));
                            }
                        }

                        break;
                    }

                case "content_block_end":
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
                                Delta: builder.ToString());
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
                    yield break;

                case "error":
                    {
                        var evt = TryDeserialize<ClaudeStreamEvent>(data);
                        var errorType = evt?.Error?.Type ?? "unknown_error";
                        var errorMessage = evt?.Error?.Message ?? data;
                        var retryAfterSeconds = TryReadRetryAfterSeconds(data);

                        throw new LlmClientException(
                            $"Claude streaming error ({errorType}): {errorMessage}",
                            statusCode: null,
                            rateLimit: retryAfterSeconds is { } seconds
                                ? new LlmRateLimitInfo(
                                    RetryAfter: TimeSpan.FromSeconds(seconds))
                                : null);
                    }
            }
        }
    }

    /// <inheritdoc />
    protected override void ValidateRequest(LlmRequest request)
    {
        base.ValidateRequest(request);

        if (request.Tools.Count > 0 &&
            request.ResponseFormat is not null)
        {
            throw new LlmRequestValidationException(
                "Claude endpoints emulate structured output with a " +
                "synthetic tool, so combining ordinary tool calls with a " +
                "response format is ambiguous and not supported. Request " +
                "either tools or structured output, not both.");
        }
    }

    private List<ClaudeTool>? ToWireTools(
        LlmRequest request)
    {
        var tools = new List<ClaudeTool>();

        if (Capabilities.NativeToolCalling && request.Tools.Count > 0)
        {
            tools.AddRange(request.Tools.Select(tool => new ClaudeTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = ParseJsonElement(
                    tool.InputSchemaJson,
                    $"tool schema '{tool.Name}'")
            }));
        }

        if (Capabilities.StructuredOutputViaTool &&
            request.ResponseFormat is not null)
        {
            tools.Add(new ClaudeTool
            {
                Name = StructuredOutputToolName,
                Description = "Return a response matching the provided JSON schema",
                InputSchema = ParseJsonElement(
                    request.ResponseFormat.Schema,
                    "response format schema")
            });
        }

        return tools.Count > 0 ? tools : null;
    }

    private static string? MapThinkingEffort(LlmThinkingEffort effort) =>
        effort switch
        {
            LlmThinkingEffort.Low => "low",
            LlmThinkingEffort.Medium => "medium",
            LlmThinkingEffort.High => "high",
            LlmThinkingEffort.Max => "high", // Claude has no "max" tier; cap at "high".
            _ => null
        };

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