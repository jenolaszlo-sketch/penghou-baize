namespace Penghou.Baize;

/// <summary>Provider-neutral reason that model generation stopped.</summary>
public enum LlmFinishReasonKind
{
    /// <summary>The provider did not report a recognized reason.</summary>
    Unknown,

    /// <summary>The model completed normally.</summary>
    Stop,

    /// <summary>The model reached an output or completion-token limit.</summary>
    LengthLimit,

    /// <summary>The model stopped to invoke one or more tools.</summary>
    ToolCall,

    /// <summary>The provider stopped generation because of content policy.</summary>
    ContentFilter,

    /// <summary>The provider reported an error as its finish reason.</summary>
    Error
}

/// <summary>Normalizes provider-specific finish-reason strings.</summary>
public static class LlmFinishReasonClassifier
{
    /// <summary>Classifies a raw or normalized provider finish reason.</summary>
    public static LlmFinishReasonKind Classify(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
            return LlmFinishReasonKind.Unknown;

        return finishReason.Trim().ToLowerInvariant() switch
        {
            "stop" or "end_turn" or "done" =>
                LlmFinishReasonKind.Stop,
            "length" or "max_tokens" or "max_output_tokens" or
                "max_completion_tokens" or "token_limit" =>
                LlmFinishReasonKind.LengthLimit,
            "tool_call" or "tool_calls" or "tool_use" =>
                LlmFinishReasonKind.ToolCall,
            "content_filter" or "safety" or "recitation" =>
                LlmFinishReasonKind.ContentFilter,
            "error" => LlmFinishReasonKind.Error,
            _ => LlmFinishReasonKind.Unknown
        };
    }
}
