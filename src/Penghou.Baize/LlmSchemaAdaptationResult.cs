using System.Text.Json;

namespace Penghou.Baize;

/// <summary>A provider-compatible schema and the changes used to produce it.</summary>
/// <param name="Schema">An owned provider-wire schema.</param>
/// <param name="Adaptations">Deterministic changes applied to the canonical schema.</param>
public sealed record LlmSchemaAdaptationResult(
    JsonElement Schema,
    IReadOnlyList<LlmSchemaAdaptation> Adaptations)
{
    /// <summary>Whether the provider representation differs from the canonical schema.</summary>
    public bool WasAdapted => Adaptations.Count > 0;

    /// <summary>Whether any adaptation weakened provider-side enforcement.</summary>
    public bool IsLossy => Adaptations.Any(item => item.IsLossy);
}
