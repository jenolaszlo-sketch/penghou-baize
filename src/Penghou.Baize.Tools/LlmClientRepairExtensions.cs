namespace Penghou.Baize.Tools;

/// <summary>Direct-client helpers for opt-in deterministic response repair.</summary>
public static class LlmClientRepairExtensions
{
    /// <summary>
    /// Wraps a direct client so schema-constrained responses are validated and
    /// deterministically repaired before their buffered content is released.
    /// Other request shapes retain their normal streaming behavior.
    /// </summary>
    public static ILlmClient WithStructuredOutputRepair(
        this ILlmClient client,
        ILlmStructuredOutputRepairer repairer,
        StructuredOutputRepairOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(repairer);
        return new StructuredOutputRepairingLlmClientDecorator(repairer, options)
            .Decorate(client);
    }
}
