namespace Penghou.Baize.Router;

/// <summary>
/// The wire protocol used to reach a model. Each endpoint pairs a model with
/// exactly one API style, so a single logical model can be reached through
/// several protocols.
/// </summary>
public enum ApiStyle
{
    /// <summary>An OpenAI-compatible chat completions API.</summary>
    OpenAi,

    /// <summary>The Anthropic Messages API.</summary>
    Claude,

    /// <summary>The native Ollama /api/chat API.</summary>
    Ollama,

    /// <summary>The Google Gemini streamGenerateContent API.</summary>
    Gemini
}

/// <summary>Compatibility helpers for built-in API styles.</summary>
public static class ApiStyleExtensions
{
    /// <summary>Converts a built-in API style to its extensible provider key.</summary>
    public static LlmProviderKey ToProviderKey(this ApiStyle apiStyle) =>
        new(apiStyle.ToString());

    /// <summary>Attempts to interpret a provider key as a built-in API style.</summary>
    public static bool TryGetApiStyle(
        this LlmProviderKey provider,
        out ApiStyle apiStyle)
    {
        if (Enum.TryParse(provider.Value, ignoreCase: true, out apiStyle) &&
            Enum.IsDefined(apiStyle))
        {
            return true;
        }

        apiStyle = default;
        return false;
    }
}
