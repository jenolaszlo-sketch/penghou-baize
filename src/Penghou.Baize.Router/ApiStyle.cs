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
