namespace Penghou.Baize.Claude;

/// <summary>
/// The extended-thinking contract used by a Claude model generation. Newer
/// Claude models accept adaptive thinking (<c>{"type":"adaptive"}</c>), where
/// the model chooses how much to reason; older manual-thinking models require
/// an explicit token budget (<c>{"type":"enabled","budget_tokens":N}</c>).
/// </summary>
public enum ClaudeThinkingStyle
{
    /// <summary>
    /// Adaptive thinking; the model decides how much to reason. Applies to
    /// current-generation Claude models such as Claude Sonnet 4.5 and Opus 4.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Manual extended thinking with a caller-supplied token budget. Applies
    /// to older Claude generations that do not support adaptive thinking.
    /// </summary>
    Manual
}
