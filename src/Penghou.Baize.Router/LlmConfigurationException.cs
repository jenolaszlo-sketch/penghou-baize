namespace Penghou.Baize.Router;

/// <summary>An actionable routing configuration failure.</summary>
public sealed class LlmConfigurationException : InvalidOperationException
{
    /// <summary>Creates a configuration exception with safe endpoint details.</summary>
    public LlmConfigurationException(
        LlmConfigurationFailureKind failureKind,
        string message,
        IReadOnlyList<LlmEndpointValidationResult>? endpointFailures = null)
        : base(message)
    {
        FailureKind = failureKind;
        EndpointFailures = endpointFailures ?? [];
    }

    /// <summary>The stage at which configuration failed.</summary>
    public LlmConfigurationFailureKind FailureKind { get; }
    /// <summary>Endpoint initialization failures; empty for structural errors.</summary>
    public IReadOnlyList<LlmEndpointValidationResult> EndpointFailures { get; }
}
