namespace Penghou.Baize;

/// <summary>
/// Capability requirements derived from a canonical request. Routers use
/// these to remove incompatible endpoints before ranking; provider clients
/// still validate again immediately before transmission.
/// </summary>
public sealed record LlmRequestRequirements
{
    /// <summary>Whether native tool calling is required.</summary>
    public bool ToolCalling { get; init; }

    /// <summary>Whether replayed history requires parallel tool calls.</summary>
    public bool ParallelToolCalls { get; init; }

    /// <summary>Whether structured output is required.</summary>
    public bool StructuredOutput { get; init; }

    /// <summary>The requested thinking configuration.</summary>
    public LlmThinkingConfig? Thinking { get; init; }

    /// <summary>Required content types and transports.</summary>
    public IReadOnlyList<LlmContentRequirement> Content { get; init; } = [];

    /// <summary>Derives requirements from a canonical request.</summary>
    public static LlmRequestRequirements From(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parts = request.Messages.SelectMany(message => message.Parts).ToArray();

        return new LlmRequestRequirements
        {
            ToolCalling = request.Tools.Count > 0 ||
                parts.Any(part => part is LlmToolCallContent or LlmToolResultContent),
            ParallelToolCalls = request.Messages.Any(message =>
                message.Parts.Count(part => part is LlmToolCallContent) > 1),
            StructuredOutput = request.ResponseFormat is not null,
            Thinking = request.ThinkingConfig,
            Content = parts
                .Select(ToContentRequirement)
                .Where(requirement => requirement is not null)
                .Select(requirement => requirement!)
                .Distinct()
                .ToArray()
        };
    }

    /// <summary>Determines whether endpoint capabilities satisfy every requirement.</summary>
    public bool IsSatisfiedBy(
        LlmEndpointCapabilities capabilities,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (ToolCalling && !capabilities.NativeToolCalling)
            return Fail("native tool calling is required", out reason);
        if (ParallelToolCalls && !capabilities.ParallelToolCalls)
            return Fail("parallel tool calls are required", out reason);
        if (StructuredOutput &&
            !capabilities.NativeStructuredOutput &&
            !capabilities.StructuredOutputViaTool)
        {
            return Fail("structured output is required", out reason);
        }
        if (ToolCalling && StructuredOutput && !capabilities.ToolsWithStructuredOutput)
            return Fail("tools combined with structured output are required", out reason);

        if (Thinking is { Mode: LlmThinkingMode.Enabled } thinking)
        {
            if (!capabilities.Thinking)
                return Fail("extended thinking is required", out reason);
            if (thinking.Effort != LlmThinkingEffort.None &&
                !capabilities.SupportedThinkingEfforts.Contains(thinking.Effort))
            {
                return Fail($"thinking effort '{thinking.Effort}' is required", out reason);
            }
        }

        if (Thinking is { Mode: LlmThinkingMode.Disabled } &&
            !capabilities.ThinkingDisable)
        {
            return Fail("explicitly disabling thinking is required", out reason);
        }

        foreach (var requirement in Content)
        {
            if (!capabilities.ContentTypes.Contains(requirement.Type))
                return Fail($"content type '{requirement.Type}' is required", out reason);

            if (requirement.Transport is { } transport)
            {
                capabilities.ContentTransports.TryGetValue(
                    requirement.Type,
                    out var supported);
                if (!supported.HasFlag(transport))
                {
                    return Fail(
                        $"transport '{transport}' for '{requirement.Type}' is required",
                        out reason);
                }
            }
        }

        reason = null;
        return true;
    }

    private static LlmContentRequirement? ToContentRequirement(LlmContentPart part) =>
        part switch
        {
            LlmTextContent or LlmReasoningContent =>
                new LlmContentRequirement(LlmContentType.Text),
            LlmImageContent image =>
                new LlmContentRequirement(LlmContentType.Image, image.Source.Transport),
            LlmAudioContent audio =>
                new LlmContentRequirement(LlmContentType.Audio, audio.Source.Transport),
            LlmVideoContent video =>
                new LlmContentRequirement(LlmContentType.Video, video.Source.Transport),
            LlmFileContent file =>
                new LlmContentRequirement(LlmContentType.File, file.Source.Transport),
            _ => null
        };

    private static bool Fail(string message, out string? reason)
    {
        reason = message;
        return false;
    }
}

/// <summary>A required content type and optional media transport.</summary>
public sealed record LlmContentRequirement(
    LlmContentType Type,
    LlmContentTransport? Transport = null);
