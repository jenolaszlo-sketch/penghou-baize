namespace Penghou.Baize;

/// <summary>
/// Stable, extensible identifier for an LLM wire provider or adapter.
/// Comparisons are case-insensitive so configuration casing is not significant.
/// </summary>
public readonly struct LlmProviderKey : IEquatable<LlmProviderKey>
{
    /// <summary>Initializes a provider key.</summary>
    /// <param name="value">The non-empty provider identifier.</param>
    public LlmProviderKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>The provider identifier.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(LlmProviderKey other) =>
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is LlmProviderKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    /// <summary>Converts a string provider identifier into a key.</summary>
    public static implicit operator LlmProviderKey(string value) => new(value);

    /// <summary>Compares two provider keys.</summary>
    public static bool operator ==(LlmProviderKey left, LlmProviderKey right) =>
        left.Equals(right);

    /// <summary>Compares two provider keys.</summary>
    public static bool operator !=(LlmProviderKey left, LlmProviderKey right) =>
        !left.Equals(right);
}
