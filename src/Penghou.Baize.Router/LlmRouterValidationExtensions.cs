namespace Penghou.Baize.Router;

/// <summary>Endpoint validation conveniences for configured routers.</summary>
public static class LlmRouterValidationExtensions
{
    /// <summary>
    /// Resolves secrets and constructs every configured provider client
    /// without sending an inference request.
    /// </summary>
    public static Task<LlmEndpointValidationReport> ValidateEndpointsAsync(
        this ILlmRouter router,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (router is not ILlmEndpointValidator validator)
        {
            throw new NotSupportedException(
                "This router does not expose configured endpoint validation. " +
                "Resolve ILlmEndpointValidator from DI when using a custom router.");
        }

        return validator.ValidateAsync(cancellationToken);
    }
}
