namespace Penghou.Baize;

/// <summary>Controls whether and how hard a model is asked to reason before answering.</summary>
public sealed class LlmThinkingConfig
{
    /// <summary>How thinking is requested relative to the provider's default.</summary>
    public LlmThinkingMode Mode { get; init; }

    /// <summary>The desired reasoning effort.</summary>
    public LlmThinkingEffort Effort { get; init; }

    /// <summary>Initializes a thinking configuration.</summary>
    /// <param name="mode">How thinking is requested.</param>
    /// <param name="effort">The desired reasoning effort.</param>
    public LlmThinkingConfig(
        LlmThinkingMode mode = LlmThinkingMode.ProviderDefault,
        LlmThinkingEffort effort = LlmThinkingEffort.High)
    {
        Mode = mode;
        Effort = effort;
    }
}

/// <summary>How a request asks a provider to treat extended thinking.</summary>
public enum LlmThinkingMode
{
    /// <summary>No preference; the provider's default behaviour applies.</summary>
    ProviderDefault,

    /// <summary>Thinking is explicitly requested.</summary>
    Enabled,

    /// <summary>Thinking is explicitly disabled; providers without an off-switch omit the setting.</summary>
    Disabled
}

/// <summary>Levels of reasoning effort a model may be asked to apply.</summary>
public enum LlmThinkingEffort
{
    /// <summary>No effort preference; the provider default is used.</summary>
    None,

    /// <summary>Minimal reasoning.</summary>
    Low,

    /// <summary>Balanced reasoning.</summary>
    Medium,

    /// <summary>High reasoning.</summary>
    High,

    /// <summary>The highest reasoning tier; providers cap this at their maximum supported level.</summary>
    Max
}
