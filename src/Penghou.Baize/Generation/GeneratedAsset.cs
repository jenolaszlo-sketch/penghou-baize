namespace Penghou.Baize.Generation;

/// <summary>
/// A single artifact produced by a generation operation. Baize never downloads
/// generated assets automatically; their <see cref="Source"/> preserves the
/// provider's URI/inline/provider-file semantics and expiration information.
/// </summary>
/// <param name="Source">How the asset is carried (URI, inline bytes, or provider file).</param>
/// <param name="ContentType">The asset content type, when known.</param>
/// <param name="FileName">The asset file name, when known.</param>
/// <param name="Size">The asset size in bytes, when known.</param>
/// <param name="ExpiresAt">When the asset (typically a temporary URL) stops being valid, when the provider conveys it.</param>
/// <param name="Metadata">Provider-specific asset metadata for diagnostics.</param>
public sealed record GeneratedAsset(
    GeneratedAssetSource Source,
    string? ContentType = null,
    string? FileName = null,
    long? Size = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);