namespace Penghou.Baize.Gemini;

internal static class GeminiUrl
{
    /// <summary>
    /// Whether a URL path segment already carries a Gemini API version (for
    /// example <c>v1beta</c>), so base URLs and batch endpoints are not
    /// double-versioned.
    /// </summary>
    internal static bool LooksLikeApiVersion(string segment) =>
        segment.Length >= 2 &&
        segment[0] == 'v' &&
        segment.Skip(1).TakeWhile(char.IsDigit).Any();
}
