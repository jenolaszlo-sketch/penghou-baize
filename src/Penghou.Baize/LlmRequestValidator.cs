namespace Penghou.Baize;

/// <summary>
/// Validates a canonical <see cref="LlmRequest"/> against an endpoint's
/// declared capabilities, throwing <see cref="LlmRequestValidationException"/>
/// for any requested feature the endpoint does not support. Shared by the
/// streaming clients (through the client base class) and the asynchronous
/// batch clients so both paths enforce the same rules before a request is
/// transmitted.
/// </summary>
public static class LlmRequestValidator
{
    /// <summary>
    /// Validates <paramref name="request"/> against
    /// <paramref name="capabilities"/>.
    /// </summary>
    /// <param name="model">The endpoint model identifier, used in error messages.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="request">The request to validate.</param>
    /// <param name="contentTypeOf">
    /// Maps a content part to the <see cref="LlmContentType"/> it carries.
    /// Defaults to a mapping that treats text and reasoning parts as text and
    /// ignores tool calls and tool results, matching the base client behavior.
    /// </param>
    /// <exception cref="LlmRequestValidationException">
    /// The request requests a feature the endpoint does not support.
    /// </exception>
    public static void Validate(
        string model,
        LlmEndpointCapabilities capabilities,
        LlmRequest request,
        Func<LlmContentPart, LlmContentType?>? contentTypeOf = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Tools.Count > 0 && !capabilities.NativeToolCalling)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support native tool calling, " +
                $"but the request declares {request.Tools.Count} tool(s).");
        }

        if (request.Tools.Any(tool => tool.Strict) &&
            !capabilities.StrictToolArguments)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support strict tool arguments, " +
                "but at least one tool requests strict schema enforcement.");
        }

        var toolCallParts = request.Messages
            .SelectMany(message => message.Parts)
            .OfType<LlmToolCallContent>()
            .ToList();
        var toolResultParts = request.Messages
            .SelectMany(message => message.Parts)
            .OfType<LlmToolResultContent>()
            .ToList();

        if ((toolCallParts.Count > 0 || toolResultParts.Count > 0) &&
            !capabilities.NativeToolCalling)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support native tool calling, " +
                "but the request replays assistant tool calls and/or tool results.");
        }

        if (!capabilities.ParallelToolCalls)
        {
            var messageWithParallelCalls = request.Messages
                .Select(message => message.Parts
                    .OfType<LlmToolCallContent>()
                    .ToList())
                .FirstOrDefault(parts => parts.Count > 1);

            if (messageWithParallelCalls is not null)
            {
                throw new LlmRequestValidationException(
                    $"Endpoint '{model}' does not support parallel tool calls, " +
                    $"but an assistant message replays {messageWithParallelCalls.Count} tool calls.");
            }
        }

        if (request.ResponseFormat is not null &&
            !capabilities.NativeStructuredOutput &&
            !capabilities.StructuredOutputViaTool)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support structured output, " +
                "but the request specifies a response format.");
        }

        if (request.Tools.Count > 0 &&
            request.ResponseFormat is not null &&
            !capabilities.ToolsWithStructuredOutput)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support combining tools with " +
                "structured output, but the request specifies both.");
        }

        if (request.ThinkingConfig is { Mode: LlmThinkingMode.Enabled } &&
            !capabilities.Thinking)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support extended thinking, " +
                "but the request enables it.");
        }

        if (request.ThinkingConfig is { Mode: LlmThinkingMode.Disabled } &&
            !capabilities.ThinkingDisable)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support disabling extended " +
                "thinking, but the request disables it.");
        }

        if (request.ThinkingConfig is
            {
                Mode: LlmThinkingMode.Enabled,
                Effort: not LlmThinkingEffort.None
            } thinking &&
            !capabilities.SupportedThinkingEfforts.Contains(thinking.Effort))
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{model}' does not support thinking effort " +
                $"'{thinking.Effort}', but the request requests it.");
        }

        var resolver = contentTypeOf ?? ContentTypeOf;

        foreach (var part in request.Messages
            .SelectMany(message => message.Parts))
        {
            var contentType = resolver(part);

            if (contentType is { } type &&
                !capabilities.ContentTypes.Contains(type))
            {
                throw new LlmRequestValidationException(
                    $"Endpoint '{model}' does not support content type " +
                    $"'{type}', but the request includes it.");
            }

            if (part is LlmMediaContent media && contentType is { } mediaType)
            {
                capabilities.ContentTransports.TryGetValue(
                    mediaType,
                    out var transports);

                if (!transports.HasFlag(media.Source.Transport))
                {
                    throw new LlmRequestValidationException(
                        $"Endpoint '{model}' does not support transport " +
                        $"'{media.Source.Transport}' for content type " +
                        $"'{mediaType}'.");
                }
            }
        }
    }

    private static LlmContentType? ContentTypeOf(LlmContentPart part) =>
        part switch
        {
            LlmTextContent => LlmContentType.Text,
            LlmReasoningContent => LlmContentType.Text,
            LlmImageContent => LlmContentType.Image,
            LlmAudioContent => LlmContentType.Audio,
            LlmVideoContent => LlmContentType.Video,
            LlmFileContent => LlmContentType.File,
            _ => null
        };
}
