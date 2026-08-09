namespace Penghou.Baize.Tools;

/// <summary>
/// Repairs the content of a completion response against a requested JSON
/// schema so that malformed structured output is fixed instead of retried.
/// Repair attempts are reported in core-owned <see cref="LlmRepairAttempt"/>
/// form so callers can decide whether to accept the response or fall back.
/// </summary>
public interface ILlmStructuredOutputRepairer
{
    /// <summary>
    /// Repairs <paramref name="response"/>'s content against
    /// <paramref name="responseFormat"/>'s schema. When the response carries no
    /// content, no schema, or content that cannot be recovered into
    /// schema-compliant JSON, the response is returned unchanged apart from the
    /// recorded <see cref="LlmResponse.ContentRepairAttempts"/>.
    /// </summary>
    /// <param name="response">The completion response to repair.</param>
    /// <param name="responseFormat">The response format the content should match.</param>
    /// <param name="cancellationToken">A token to cancel the repair.</param>
    /// <returns>The repaired response.</returns>
    Task<LlmResponse> RepairAsync(
        LlmResponse response,
        LlmResponseFormat responseFormat,
        CancellationToken cancellationToken = default);
}
