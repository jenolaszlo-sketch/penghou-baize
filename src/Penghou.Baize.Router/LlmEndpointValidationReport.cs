namespace Penghou.Baize.Router;

/// <summary>The validation outcome for the current routing snapshot.</summary>
/// <param name="Endpoints">One safe result per configured endpoint.</param>
public sealed record LlmEndpointValidationReport(
    IReadOnlyList<LlmEndpointValidationResult> Endpoints)
{
    /// <summary>Whether every configured endpoint initialized successfully.</summary>
    public bool Succeeded => Endpoints.All(endpoint => endpoint.Succeeded);
}

/// <summary>A safe endpoint initialization result containing no credentials.</summary>
/// <param name="EndpointId">The configured endpoint identifier.</param>
/// <param name="Provider">The provider adapter key.</param>
/// <param name="Model">The provider model identifier.</param>
/// <param name="Succeeded">Whether provider construction succeeded.</param>
/// <param name="Error">The initialization error, when validation failed.</param>
public sealed record LlmEndpointValidationResult(
    string EndpointId,
    string Provider,
    string Model,
    bool Succeeded,
    string? Error = null);
