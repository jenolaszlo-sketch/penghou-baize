namespace Penghou.Baize;

/// <summary>
/// Opaque provider-specific metadata required to faithfully replay a content
/// part on a later turn. Providers like Gemini require the exact thought
/// signature and function-call IDs from a previous response to be echoed back
/// on the next request; without them a conversation can fail or misbehave.
/// Values are provider-defined and keyed by the provider that produced them.
/// </summary>
/// <param name="Provider">The provider the metadata belongs to.</param>
/// <param name="Values">The provider-defined continuation values.</param>
public sealed record LlmProviderContinuation(
    string Provider,
    IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Whether this metadata belongs to the named provider.</summary>
    /// <param name="provider">The provider name to compare.</param>
    /// <returns><c>true</c> when the provider names match.</returns>
    public bool IsFor(string provider) =>
        string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The continuation value for <paramref name="key"/>, or null when the
    /// provider did not supply one.
    /// </summary>
    /// <param name="key">The continuation value key.</param>
    /// <returns>The value, or null when absent.</returns>
    public string? GetValue(string key) =>
        Values.TryGetValue(key, out var value)
            ? value
            : null;
}
