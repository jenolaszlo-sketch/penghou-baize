namespace Penghou.Baize.OpenAi;

/// <summary>
/// The wire dialect of an OpenAI-compatible endpoint. Providers in the
/// OpenAI-compatible family differ in which request parameters they accept
/// (for example whether an explicit thinking toggle is valid), so the dialect
/// is declared per endpoint instead of being inferred from the model name.
/// </summary>
public enum OpenAiDialect
{
    /// <summary>The standard OpenAI Chat Completions wire contract.</summary>
    Standard,

    /// <summary>
    /// DeepSeek's OpenAI-compatible contract, which accepts an explicit
    /// <c>thinking</c> toggle (<c>enabled</c> or <c>disabled</c>) on the
    /// request and streams reasoning through <c>reasoning_content</c>.
    /// </summary>
    DeepSeek
}
