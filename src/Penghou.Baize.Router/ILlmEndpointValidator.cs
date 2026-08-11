namespace Penghou.Baize.Router;

/// <summary>
/// Validates configured endpoints without sending an inference request.
/// Validation resolves secrets and constructs provider clients, surfacing
/// configuration failures before the first real completion.
/// </summary>
public interface ILlmEndpointValidator
{
    /// <summary>Validates every endpoint in the current routing snapshot.</summary>
    Task<LlmEndpointValidationReport> ValidateAsync(
        CancellationToken cancellationToken = default);
}
