namespace Penghou.Baize.Router;

/// <summary>An actionable, structured route-resolution failure.</summary>
public sealed class LlmRoutingException : InvalidOperationException
{
    /// <summary>Initializes a structured routing failure.</summary>
    public LlmRoutingException(
        string message,
        LlmRoutingFailureKind failureKind,
        LlmRouteTarget target,
        IReadOnlyList<string>? configuredModels = null,
        IReadOnlyList<LlmRouteCandidateExplanation>? candidates = null)
        : base(message)
    {
        FailureKind = failureKind;
        Target = target;
        ConfiguredModels = configuredModels ?? [];
        Candidates = candidates ?? [];
    }

    /// <summary>The machine-readable failure category.</summary>
    public LlmRoutingFailureKind FailureKind { get; }

    /// <summary>The requested route target.</summary>
    public LlmRouteTarget Target { get; }

    /// <summary>The configured model chain involved in the failure.</summary>
    public IReadOnlyList<string> ConfiguredModels { get; }

    /// <summary>Endpoint compatibility details, when candidates were expanded.</summary>
    public IReadOnlyList<LlmRouteCandidateExplanation> Candidates { get; }
}
