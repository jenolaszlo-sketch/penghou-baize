using System.Text.Json;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// Builds Gemini generateContent wire requests from canonical
/// <see cref="LlmRequest"/> instances and enforces the request rules. Shared
/// by the streaming <see cref="GeminiChatClient"/> and the asynchronous
/// <see cref="GeminiBatchClient"/> so batch items carry exactly the same wire
/// shape as streaming calls (Gemini selects streaming via the URL method, so
/// no body flag distinguishes the two).
/// </summary>
internal static class GeminiMessageRequestMapper
{
    /// <summary>
    /// Builds a Gemini generateContent wire request. The model is carried in
    /// the URL path, so it does not appear in the body.
    /// </summary>
    /// <param name="model">The endpoint model identifier.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="request">The canonical request.</param>
    /// <param name="schemaAdapter">Adapter for the provider's JSON Schema dialect.</param>
    /// <param name="apiVersion">The Gemini API version receiving the request.</param>
    /// <returns>The Gemini wire request.</returns>
    public static GeminiChatRequest Build(
        string model,
        LlmEndpointCapabilities capabilities,
        LlmRequest request,
        ILlmSchemaAdapter? schemaAdapter = null,
        string? apiVersion = null)
    {
        var contents = new List<GeminiChatMessage>();
        var systemText = new List<string>();

        foreach (var message in request.Messages)
        {
            var isSystem =
                string.Equals(
                    message.Role,
                    "system",
                    StringComparison.OrdinalIgnoreCase);

            if (isSystem)
            {
                foreach (var part in message.Parts)
                {
                    if (part is not LlmTextContent text)
                    {
                        throw new LlmRequestValidationException(
                            "Gemini accepts only text in the system " +
                            "instruction; a system message carries a " +
                            "non-text content part.");
                    }

                    systemText.Add(text.Text);
                }

                continue;
            }

            var role =
                string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    ? "user"
                    : string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "model"
                        : message.Role;
            var parts = new List<GeminiContentPart>();

            foreach (var part in message.Parts)
            {
                switch (part)
                {
                    case LlmTextContent text:
                        parts.Add(
                            new GeminiContentPart
                            {
                                Text = text.Text,
                                ThoughtSignature =
                                    GeminiContinuation(text.Continuation)
                                        ?.GetValue("thoughtSignature")
                            });
                        break;

                    case LlmReasoningContent reasoning:
                        var continuation = GeminiContinuation(
                            reasoning.Continuation);

                        if (continuation is null)
                            break;

                        parts.Add(
                            new GeminiContentPart
                            {
                                Text = reasoning.Text,
                                Thought = true,
                                ThoughtSignature =
                                    continuation.GetValue("thoughtSignature")
                            });
                        break;

                    case LlmToolCallContent toolCall:
                        parts.Add(
                            new GeminiContentPart
                            {
                                ThoughtSignature =
                                    GeminiContinuation(
                                        toolCall.ToolCall.Continuation ??
                                        toolCall.Continuation)?.GetValue(
                                            "thoughtSignature"),
                                FunctionCall =
                                    new GeminiFunctionCall
                                    {
                                        Id = toolCall.ToolCall.Id,
                                        Name = toolCall.ToolCall.Name,
                                        Args = ParseJsonElement(
                                            toolCall.ToolCall.ArgumentsJson,
                                            $"tool call '{toolCall.ToolCall.Name}' arguments")
                                    }
                            });
                        break;

                    case LlmToolResultContent toolResult:
                        parts.Add(
                            new GeminiContentPart
                            {
                                FunctionResponse =
                                    new GeminiFunctionResponse
                                    {
                                        Id = toolResult.Result.ToolCallId,
                                        Name = toolResult.Result.ToolName,
                                        Response = ToJsonValue(
                                            toolResult.Result.Content)
                                    }
                            });
                        break;

                    case LlmMediaContent media:
                        parts.Add(ToMediaPart(media));
                        break;
                }
            }

            if (parts.Count == 0)
                continue;

            contents.Add(
                new GeminiChatMessage
                {
                    Role = role,
                    Parts = parts
                });
        }

        var systemInstruction = systemText.Count == 0
            ? null
            : new GeminiSystemInstruction
            {
                Parts =
                [
                    new GeminiContentPart
                    {
                        Text = string.Join(
                            Environment.NewLine + Environment.NewLine,
                            systemText)
                    }
                ]
            };

        var responseSchema = request.ResponseFormat?.Schema is null
            ? (JsonElement?)null
            : AdaptSchema(
                ParseJsonElement(
                    request.ResponseFormat.Schema,
                    "response format schema"),
                schemaAdapter,
                model,
                apiVersion,
                LlmSchemaPurpose.StructuredResponse);

        return new GeminiChatRequest
        {
            Contents = contents,
            SystemInstruction = systemInstruction,
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxTokens,
                ResponseSchema = responseSchema,
                ResponseMimeType =
                    request.ResponseFormat is null ? null : "application/json",
                ThinkingConfig = MapThinkingConfig(
                    model,
                    capabilities,
                    request.ThinkingConfig)
            },
            Tools = capabilities.NativeToolCalling && request.Tools.Count > 0
                ? request.Tools
                    .Select(tool => ToWireTool(
                        tool,
                        schemaAdapter,
                        model,
                        apiVersion))
                    .ToList()
                : null
        };
    }

    private static GeminiContentPart ToMediaPart(LlmMediaContent media) =>
        media.Source switch
        {
            LlmInlineDataSource inline => new GeminiContentPart
            {
                InlineData = new GeminiInlineData
                {
                    MimeType = media.MediaType,
                    Data = Convert.ToBase64String(inline.Data.Span)
                }
            },
            LlmUriSource uri => new GeminiContentPart
            {
                FileData = new GeminiFileData
                {
                    MimeType = media.MediaType,
                    FileUri = uri.Uri.AbsoluteUri
                }
            },
            LlmProviderFileSource file when file.Provider == new LlmProviderKey("Gemini") =>
                new GeminiContentPart
                {
                    FileData = new GeminiFileData
                    {
                        MimeType = media.MediaType,
                        FileUri = file.FileId
                    }
                },
            LlmProviderFileSource file => throw new LlmRequestValidationException(
                $"Gemini cannot use a file owned by provider '{file.Provider}'."),
            _ => throw new LlmRequestValidationException(
                $"Gemini does not support media source '{media.Source.GetType().Name}'.")
        };

    /// <summary>
    /// Validates a request against the endpoint capabilities before it is
    /// transmitted on either the streaming or the batch path.
    /// </summary>
    /// <param name="model">The endpoint model identifier, used in error messages.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="request">The request to validate.</param>
    public static void Validate(
        string model,
        LlmEndpointCapabilities capabilities,
        LlmRequest request) =>
        LlmRequestValidator.Validate(model, capabilities, request);

    /// <summary>
    /// Maps an explicit thinking request to the <c>thinkingConfig</c> block.
    /// Enabling is expressed as a token budget; disabling as a zero budget.
    /// A missing effort with no configured budget cannot be expressed, so it
    /// is rejected rather than silently emitting no thinking configuration.
    /// </summary>
    private static GeminiThinkingConfig? MapThinkingConfig(
        string model,
        LlmEndpointCapabilities capabilities,
        LlmThinkingConfig? config)
    {
        if (config is null || config.Mode == LlmThinkingMode.ProviderDefault)
        {
            return null;
        }

        if (config.Mode == LlmThinkingMode.Disabled)
        {
            return new GeminiThinkingConfig { ThinkingBudget = 0 };
        }

        return MapThinkingBudget(capabilities, config.Effort) is { } budget
            ? new GeminiThinkingConfig { ThinkingBudget = budget }
            : throw new LlmRequestValidationException(
                $"Endpoint '{model}' needs a Gemini thinking token budget " +
                "(set Capabilities.ThinkingBudget or request a concrete " +
                "effort instead of 'None').");
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
            LlmThinkingEffort.High => 16384,
            // Gemini has no explicit "max" tier; use the largest documented
            // budget (32768 for Gemini 2.5 Pro). Prefer an explicit
            // Capabilities.ThinkingBudget to match the exact model range.
            LlmThinkingEffort.Max => 32768,
            _ => null
        };

    private static GeminiTool ToWireTool(
        LlmTool tool,
        ILlmSchemaAdapter? schemaAdapter,
        string model,
        string? apiVersion)
    {
        return new GeminiTool
        {
            FunctionDeclarations = new List<GeminiFunctionDeclaration>
            {
                new GeminiFunctionDeclaration
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = AdaptSchema(
                        ParseJsonElement(
                            tool.InputSchemaJson,
                            $"tool schema '{tool.Name}'"),
                        schemaAdapter,
                        model,
                        apiVersion,
                        LlmSchemaPurpose.ToolInput)
                }
            }
        };
    }

    private static JsonElement AdaptSchema(
        JsonElement schema,
        ILlmSchemaAdapter? adapter,
        string model,
        string? apiVersion,
        LlmSchemaPurpose purpose) =>
        (adapter ?? GeminiSchemaAdapter.Default)
            .Adapt(
                schema,
                new LlmSchemaAdaptationContext(
                    new LlmProviderKey("Gemini"),
                    model,
                    apiVersion,
                    purpose))
            .Schema;

    private static LlmProviderContinuation? GeminiContinuation(
        LlmProviderContinuation? continuation) =>
        continuation is not null && continuation.IsFor("Gemini")
            ? continuation
            : null;

    /// <summary>
    /// Serializes tool-result text as a JSON value: valid JSON is preserved as
    /// an object/array, anything else is emitted as a JSON string.
    /// </summary>
    private static JsonElement ToJsonValue(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }

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
