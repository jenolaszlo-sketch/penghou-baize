namespace Penghou.Baize.Generation;

/// <summary>The artifacts produced by a successful generation operation.</summary>
/// <param name="Assets">The generated assets.</param>
/// <param name="Metadata">Provider-specific result metadata for diagnostics.</param>
public sealed record GenerationResult(
    IReadOnlyList<GeneratedAsset> Assets,
    IReadOnlyDictionary<string, object?>? Metadata = null);