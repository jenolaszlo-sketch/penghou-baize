using System.Text.Json;
using Penghou.Baize;

namespace Penghou.Baize.Claude;

/// <summary>
/// Builds Claude Messages wire requests from canonical <see cref="LlmRequest"/>
/// instances and enforces the Claude-specific request rules. Shared by the
/// streaming <see cref="ClaudeChatClient"/> and the asynchronous
/// <see cref="ClaudeBatchClient"/> so batch items carry exactly the same wire
/// shape as streaming calls (except for the streaming flag, which batch items
/// must not set).
/// </summary>
internal static class ClaudeMessageRequestMapper
{
    /// <summary>
    /// The synthetic tool Claude endpoints use to emulate JSON-schema output.
    /// Its payload is repackaged as content so it never surfaces as a call.
    /// </summary>
    internal const string StructuredOutputToolName = "structured_output";

    private const int DefaultMaxTokens = 4096;

    /// <summary>
    /// Builds a Claude Messages wire request.
    /// </summary>
    /// <param name="model">The endpoint model identifier.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="thinkingStyle">The extended-thinking contract of the model generation.</param>
    /// <param name="request">The canonical request.</param>
    /// <param name="streaming">
    /// Whether the request targets the streaming messages endpoint. Batch items
    /// are never streamed and omit the streaming flag.
    /// </param>
    /// <returns>The Claude wire request.</returns>
    public static ClaudeMessageRequest Build(
        string model,
        LlmEndpointCapabilities capabilities,
        ClaudeThinkingStyle thinkingStyle,
        LlmRequest request,
        bool streaming)
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
            Model = model,
            System = string.IsNullOrEmpty(system)
                ? null
                : system,
            Messages = nonSystemMessages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens ?? DefaultMaxTokens,
            Stream = streaming ? true : null,
            Tools = ToWireTools(capabilities, request),
            Thinking = MapThinking(
                model,
                capabilities,
                thinkingStyle,
                request.ThinkingConfig),
            OutputConfig = MapOutputConfig(
                thinkingStyle,
                request.ThinkingConfig)
        };
    }

    /// <summary>
    /// Validates a request against the endpoint capabilities, applying the
    /// Claude-specific rule that ordinary tool calls cannot be combined with a
    /// response format (both would claim the synthetic structured-output tool).
    /// </summary>
    /// <param name="model">The endpoint model identifier, used in error messages.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="request">The request to validate.</param>
    public static void Validate(
        string model,
        LlmEndpointCapabilities capabilities,
        LlmRequest request)
    {
        LlmRequestValidator.Validate(model, capabilities, request);

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

    /// <summary>
    /// Emits the <c>thinking</c> block for an explicit request. Adaptive
    /// models receive <c>{"type":"adaptive"}</c>; manual-thinking models
    /// receive <c>{"type":"enabled","budget_tokens":N}</c> where the budget
    /// comes from <see cref="LlmEndpointCapabilities.ThinkingBudget"/> or is
    /// derived from the requested effort. Disabling is encoded as
    /// <c>{"type":"disabled"}</c> so the capability is exercised faithfully.
    /// </summary>
    private static ClaudeThinking? MapThinking(
        string model,
        LlmEndpointCapabilities capabilities,
        ClaudeThinkingStyle thinkingStyle,
        LlmThinkingConfig? config)
    {
        if (config is null || config.Mode == LlmThinkingMode.ProviderDefault)
        {
            return null;
        }

        if (config.Mode == LlmThinkingMode.Disabled)
        {
            return new ClaudeThinking { Type = "disabled" };
        }

        if (thinkingStyle == ClaudeThinkingStyle.Adaptive)
        {
            return new ClaudeThinking { Type = "adaptive" };
        }

        // Manual thinking requires a concrete token budget. A missing effort
        // with no configured budget cannot be expressed; reject rather than
        // silently emitting no thinking block for an enabled request.
        return MapThinkingBudget(capabilities, config.Effort) is { } budget
            ? new ClaudeThinking
            {
                Type = "enabled",
                BudgetTokens = budget
            }
            : throw new LlmRequestValidationException(
                $"Endpoint '{model}' uses manual thinking, which requires a " +
                "token budget (set Capabilities.ThinkingBudget or request a " +
                "concrete effort instead of 'None').");
    }

    /// <summary>
    /// Emits the <c>output_config</c> block for adaptive thinking. Manual
    /// thinking is controlled by the token budget instead, so effort is not
    /// expressed there.
    /// </summary>
    private static ClaudeOutputConfig? MapOutputConfig(
        ClaudeThinkingStyle thinkingStyle,
        LlmThinkingConfig? config)
    {
        if (config is null ||
            config.Mode != LlmThinkingMode.Enabled ||
            thinkingStyle == ClaudeThinkingStyle.Manual)
        {
            return null;
        }

        return MapThinkingEffort(config.Effort) is { } effort
            ? new ClaudeOutputConfig { Effort = effort }
            : null;
    }

    private static int? MapThinkingBudget(
        LlmEndpointCapabilities capabilities,
        LlmThinkingEffort effort) =>
        capabilities.ThinkingBudget ?? EffortToBudget(effort);

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

    private static IEnumerable<ClaudeMessage> ToWireMessages(LlmMessage message)
    {
        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            var results = message.Parts
                .OfType<LlmToolResultContent>()
                .Select(part => part.Result)
                .ToList();

            if (results.Count == 0)
                yield break;

            // All results of one tool turn belong in a single immediately
            // following user message, so parallel tool calls are preserved
            // (separate messages can flush parallel behaviour or produce
            // invalid history).
            yield return new ClaudeMessage
            {
                Role = "user",
                Content = results
                    .Select(result => new ClaudeContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = result.ToolCallId,
                        Content = result.Content,
                        IsError = !result.Succeeded
                    })
                    .ToList()
            };

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

                case LlmReasoningContent reasoning:
                    var continuation = reasoning.Continuation;

                    // Thinking blocks are model/provider-bound. Foreign or
                    // unsigned reasoning must not be reconstructed as Claude
                    // thinking because Anthropic validates the opaque payload.
                    if (continuation is null || !continuation.IsFor("Claude"))
                        break;

                    if (continuation.GetValue("redactedThinkingData") is { } data)
                    {
                        blocks.Add(new ClaudeContentBlock
                        {
                            Type = "redacted_thinking",
                            Data = data
                        });
                    }
                    else if (continuation.GetValue("signature") is { } signature)
                    {
                        blocks.Add(new ClaudeContentBlock
                        {
                            Type = "thinking",
                            Thinking = reasoning.Text,
                            Signature = signature
                        });
                    }

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

                case LlmImageContent image:
                    blocks.Add(new ClaudeContentBlock
                    {
                        Type = "image",
                        Source = ToMediaSource(image)
                    });
                    break;

                case LlmFileContent file:
                    blocks.Add(new ClaudeContentBlock
                    {
                        Type = "document",
                        Source = ToMediaSource(file)
                    });
                    break;

                case LlmMediaContent media:
                    throw new LlmRequestValidationException(
                        $"Claude Messages does not support content type " +
                        $"'{media.GetType().Name}'.");
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

    private static ClaudeContentSource ToMediaSource(LlmMediaContent media) =>
        media.Source switch
        {
            LlmInlineDataSource inline => new ClaudeContentSource
            {
                Type = "base64",
                MediaType = media.MediaType,
                Data = Convert.ToBase64String(inline.Data.Span)
            },
            LlmUriSource uri => new ClaudeContentSource
            {
                Type = "url",
                Url = uri.Uri.AbsoluteUri
            },
            LlmProviderFileSource file when
                file.Provider == new LlmProviderKey("Claude") =>
                new ClaudeContentSource
                {
                    Type = "file",
                    FileId = file.FileId
                },
            LlmProviderFileSource file => throw new LlmRequestValidationException(
                $"Claude cannot use a file owned by provider '{file.Provider}'."),
            _ => throw new LlmRequestValidationException(
                $"Claude does not support media source '{media.Source.GetType().Name}'.")
        };

    private static List<ClaudeTool>? ToWireTools(
        LlmEndpointCapabilities capabilities,
        LlmRequest request)
    {
        var tools = new List<ClaudeTool>();

        if (capabilities.NativeToolCalling && request.Tools.Count > 0)
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

        if (capabilities.StructuredOutputViaTool &&
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
            LlmThinkingEffort.None => null,
            LlmThinkingEffort.Low => "low",
            LlmThinkingEffort.Medium => "medium",
            LlmThinkingEffort.High => "high",
            // Claude has no "max" effort on the wire; reject rather than
            // silently capping to "high".
            LlmThinkingEffort.Max => throw new LlmRequestValidationException(
                "Claude does not support a 'max' reasoning effort; it would " +
                "be silently capped to 'high'."),
            _ => null
        };

    private static JsonElement ParseJsonElement(
        string? json,
        string context)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new LlmClientException($"Missing JSON for {context}.");

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new LlmClientException($"Failed to parse {context}: {json}", ex);
        }
    }
}
