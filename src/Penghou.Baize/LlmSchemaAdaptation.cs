namespace Penghou.Baize;

/// <summary>Describes one provider-wire change made to a canonical schema.</summary>
/// <param name="Path">JSON path of the affected schema keyword.</param>
/// <param name="Keyword">The affected JSON Schema keyword.</param>
/// <param name="Action">The adaptation action, for example <c>removed</c>.</param>
/// <param name="Reason">Why the provider wire dialect required the change.</param>
/// <param name="IsLossy">Whether provider-side enforcement became weaker.</param>
public sealed record LlmSchemaAdaptation(
    string Path,
    string Keyword,
    string Action,
    string Reason,
    bool IsLossy);
