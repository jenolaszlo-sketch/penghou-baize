using System.Text.Json;
using Penghou.Baize;

namespace Penghou.Baize.OpenAi;

/// <summary>
/// Builds OpenAI Chat Completions wire requests from canonical
/// <see cref="LlmRequest"/> instances. Shared by the streaming
/// <see cref="OpenAiChatClient"/> and the asynchronous
/// <see cref="OpenAiBatchClient"/> so batch items carry exactly the same wire
/// shape as streaming calls (except for the streaming fields, which batch
/// items must not include).
/// </summary>
internal static class OpenAiChatCompletionRequestMapper
{
    /// <summary>
    /// Builds an OpenAI Chat Completions wire request.
    /// </summary>
    /// <param name="model">The endpoint model identifier.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="dialect">The wire dialect of the endpoint.</param>
    /// <param name="request">The canonical request.</param>
    /// <param name="streaming">
    /// Whether the request targets the streaming chat endpoint. Batch items are
    /// always non-streaming and omit the streaming fields.
    /// </param>
    /// <returns>The OpenAI wire request.</returns>
    public static OpenAiChatCompletionRequest Build(
        string model,
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect,
        LlmRequest request,
        bool streaming)
    {
        Validate(model, capabilities, dialect, request);
        var usesSyntheticTool = OpenAiStructuredOutput.UsesSyntheticTool(
            capabilities,
            request);
        var messages = request.Messages
            .SelectMany(message => ToWireMessages(dialect, message))
            .ToList();

        if (dialect == OpenAiDialect.DeepSeek &&
            request.ResponseFormat is { Type: "json_object" })
        {
            messages.Insert(0, new OpenAiChatMessage
            {
                Role = "system",
                Content = "Return valid JSON only. Do not include Markdown or any text outside the JSON value."
            });
        }

        return new OpenAiChatCompletionRequest
        {
            Model = model,
            Messages = messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = streaming ? true : null,
            StreamOptions = streaming
                ? new OpenAiStreamOptions { IncludeUsage = true }
                : null,
            Tools = ToWireTools(capabilities, request, usesSyntheticTool),
            ToolChoice = usesSyntheticTool
                ? new
                {
                    type = "function",
                    function = new { name = OpenAiStructuredOutput.ToolName }
                }
                : null,
            ResponseFormat = usesSyntheticTool
                ? null
                : MapResponseFormat(request.ResponseFormat),
            ReasoningEffort = request.ThinkingConfig is null || request.ThinkingConfig.Mode != LlmThinkingMode.Enabled
                ? null
                : MapThinkingEffort(request.ThinkingConfig.Effort),
            Thinking = usesSyntheticTool && dialect == OpenAiDialect.DeepSeek
                ? new { type = "disabled" }
                : MapThinkingToggle(dialect, request.ThinkingConfig)
        };
    }

    private static List<OpenAiTool>? ToWireTools(
        LlmEndpointCapabilities capabilities,
        LlmRequest request,
        bool usesSyntheticTool)
    {
        var tools = new List<OpenAiTool>();

        if (capabilities.NativeToolCalling)
        {
            tools.AddRange(request.Tools.Select(tool => new OpenAiTool
            {
                Function = new OpenAiFunctionTool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = ParseJsonElement(
                        tool.InputSchemaJson,
                        $"tool schema '{tool.Name}'"),
                    Strict = tool.Strict ? true : null
                }
            }));
        }

        if (usesSyntheticTool)
        {
            tools.Add(new OpenAiTool
            {
                Function = new OpenAiFunctionTool
                {
                    Name = OpenAiStructuredOutput.ToolName,
                    Description = "Return a response matching the provided JSON schema",
                    Parameters = ParseJsonElement(
                        request.ResponseFormat!.Schema,
                        "response format schema")
                }
            });
        }

        return tools.Count > 0 ? tools : null;
    }

    private static void Validate(
        string model,
        LlmEndpointCapabilities capabilities,
        OpenAiDialect dialect,
        LlmRequest request)
    {
        LlmRequestValidator.Validate(model, capabilities, request);

        if (OpenAiStructuredOutput.UsesSyntheticTool(capabilities, request) &&
            request.Tools.Count > 0)
        {
            throw new LlmRequestValidationException(
                "This OpenAI-compatible endpoint emulates structured output " +
                "with a synthetic tool, so ordinary tools and a response " +
                "schema cannot be combined in one request.");
        }

        if (OpenAiStructuredOutput.UsesSyntheticTool(capabilities, request) &&
            dialect == OpenAiDialect.DeepSeek &&
            request.ThinkingConfig is { Mode: LlmThinkingMode.Enabled })
        {
            throw new LlmRequestValidationException(
                "DeepSeek-style forced tool output cannot be combined with " +
                "explicit thinking. Remove the thinking request or use plain " +
                "JSON mode and validate the result locally.");
        }

        if (request.Tools.Any(tool =>
                string.Equals(
                    tool.Name,
                    OpenAiStructuredOutput.ToolName,
                    StringComparison.Ordinal)))
        {
            throw new LlmRequestValidationException(
                $"Tool name '{OpenAiStructuredOutput.ToolName}' is reserved " +
                "for tool-backed structured output.");
        }
    }

    private static object? MapResponseFormat(LlmResponseFormat? format) =>
        format switch
        {
            null => null,
            { Type: "json_object" } => new { type = "json_object" },
            _ => new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "response",
                    schema = ParseJsonElement(
                        format.Schema,
                        "response format schema"),
                    strict = true
                }
            }
        };

    private static object? MapThinkingToggle(
        OpenAiDialect dialect,
        LlmThinkingConfig? config)
    {
        if (config is null || config.Mode == LlmThinkingMode.ProviderDefault)
        {
            return null;
        }

        if (dialect != OpenAiDialect.DeepSeek)
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

    private static IEnumerable<OpenAiChatMessage> ToWireMessages(
        OpenAiDialect dialect,
        LlmMessage message)
    {
        var text = string.Concat(
            message.Parts
                .OfType<LlmTextContent>()
                .Select(part => part.Text));
        var reasoning = dialect == OpenAiDialect.DeepSeek
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
        var media = message.Parts.OfType<LlmMediaContent>().ToList();
        object? content = media.Count == 0
            ? string.IsNullOrEmpty(text) ? null : text
            : ToMultimodalContent(text, media);

        if (toolCalls.Count > 0)
        {
            yield return new OpenAiChatMessage
            {
                Role = message.Role,
                Content = content,
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
            Content = content,
            ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning
        };
    }

    private static IReadOnlyList<OpenAiMessageContentPart> ToMultimodalContent(
        string text,
        IReadOnlyList<LlmMediaContent> media)
    {
        var result = new List<OpenAiMessageContentPart>();

        if (!string.IsNullOrEmpty(text))
            result.Add(new OpenAiMessageContentPart { Type = "text", Text = text });

        result.AddRange(media.Select(ToMultimodalContentPart));
        return result;
    }

    private static OpenAiMessageContentPart ToMultimodalContentPart(
        LlmMediaContent media) => media switch
        {
            LlmImageContent image => new OpenAiMessageContentPart
            {
                Type = "image_url",
                ImageUrl = new OpenAiImageUrl
                {
                    Url = image.Source switch
                    {
                        LlmUriSource uri => uri.Uri.AbsoluteUri,
                        LlmInlineDataSource inline => DataUri(image.MediaType, inline),
                        _ => throw UnsupportedMediaSource("image", image.Source)
                    }
                }
            },
            LlmAudioContent audio when audio.Source is LlmInlineDataSource inline =>
                new OpenAiMessageContentPart
                {
                    Type = "input_audio",
                    InputAudio = new OpenAiInputAudio
                    {
                        Data = Convert.ToBase64String(inline.Data.Span),
                        Format = AudioFormat(audio.MediaType)
                    }
                },
            LlmFileContent file => ToFilePart(file),
            _ => throw new LlmRequestValidationException(
                $"OpenAI Chat Completions does not support content type " +
                $"'{media.GetType().Name}' with transport " +
                $"'{media.Source.Transport}'.")
        };

    private static OpenAiMessageContentPart ToFilePart(LlmFileContent file) =>
        file.Source switch
        {
            LlmProviderFileSource source when
                source.Provider == new LlmProviderKey("OpenAi") =>
                new OpenAiMessageContentPart
                {
                    Type = "file",
                    File = new OpenAiInputFile
                    {
                        FileId = source.FileId,
                        FileName = file.FileName
                    }
                },
            LlmInlineDataSource source => new OpenAiMessageContentPart
            {
                Type = "file",
                File = new OpenAiInputFile
                {
                    FileData = DataUri(file.MediaType, source),
                    FileName = file.FileName
                }
            },
            _ => throw UnsupportedMediaSource("file", file.Source)
        };

    private static string DataUri(string mediaType, LlmInlineDataSource source) =>
        $"data:{mediaType};base64,{Convert.ToBase64String(source.Data.Span)}";

    private static string AudioFormat(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/mpeg" or "audio/mp3" => "mp3",
        _ => throw new LlmRequestValidationException(
            $"OpenAI Chat Completions does not support inline audio media type '{mediaType}'.")
    };

    private static LlmRequestValidationException UnsupportedMediaSource(
        string kind,
        LlmMediaSource source) =>
        new($"OpenAI Chat Completions does not support {kind} source " +
            $"'{source.GetType().Name}'.");

    private static string? MapThinkingEffort(LlmThinkingEffort effort) =>
        LlmThinking.MapStandardEffort("OpenAI", effort);

    private static JsonElement ParseJsonElement(
        string? json,
        string context) =>
        LlmJson.ParseElement(json, context);
}
