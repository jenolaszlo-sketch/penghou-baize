using System.Text.Json;

namespace Penghou.Baize;

/// <summary>
/// Shared JSON parsing helpers for provider adapters.
/// </summary>
public static class LlmJson
{
    private const int MaximumErrorTextLength = 1024;

    /// <summary>Returns bounded text suitable for exception messages.</summary>
    public static string FormatForError(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= MaximumErrorTextLength
            ? value
            : $"{value[..MaximumErrorTextLength]}…";
    }

    /// <summary>
    /// Returns a bounded provider URL without its query or fragment, which may
    /// contain signed credentials.
    /// </summary>
    public static string FormatUrlForError(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var delimiter = value.IndexOfAny(['?', '#']);
        var sanitized = delimiter >= 0 ? value[..delimiter] : value;
        return FormatForError(sanitized);
    }

    /// <summary>
    /// Parses a JSON string into an owned, document-independent
    /// <see cref="JsonElement"/>, throwing a <see cref="LlmClientException"/>
    /// with the given <paramref name="context"/> on missing or malformed input.
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <param name="context">A description of where the JSON came from, used in error messages.</param>
    /// <returns>An owned clone of the parsed root element.</returns>
    public static JsonElement ParseElement(
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
            throw new LlmClientException(
                $"Failed to parse {context}: {FormatForError(json)}",
                ex);
        }
    }
}
