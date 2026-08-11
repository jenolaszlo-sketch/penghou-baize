using System.Text.Json;

namespace Penghou.Baize;

/// <summary>
/// Adapts canonical JSON Schema to a provider's wire dialect without modifying
/// the authoritative schema retained by Baize for local validation.
/// </summary>
public interface ILlmSchemaAdapter
{
    /// <summary>Creates an owned provider-wire schema and reports every change.</summary>
    /// <param name="schema">The canonical JSON Schema.</param>
    /// <param name="context">Provider, model, API-version, and purpose context.</param>
    /// <returns>The adapted schema and its deterministic change report.</returns>
    LlmSchemaAdaptationResult Adapt(
        JsonElement schema,
        LlmSchemaAdaptationContext context);
}
