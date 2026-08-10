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
        return new OpenAiChatCompletionRequest
        {
            Model = model,
            Messages = request.Messages
                .SelectMany(message => ToWireMessages(dialect, message))
                .ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = streaming ? true : null,
            StreamOptions = streaming
                ? new OpenAiStreamOptions { IncludeUsage = true }
                : null,
            Tools = !capabilities.NativeToolCalling
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
            Thinking = MapThinkingToggle(dialect, request.ThinkingConfig)
        };
    }

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
