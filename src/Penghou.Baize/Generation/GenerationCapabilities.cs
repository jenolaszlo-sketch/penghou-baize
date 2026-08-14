namespace Penghou.Baize.Generation;

/// <summary>
/// Declares what a single configured generation endpoint/model can do.
/// Capabilities describe the configured endpoint, not the vendor's entire
/// catalog, so routing and pre-flight validation see a conservative surface.
/// </summary>
public sealed record GenerationCapabilities
{
    /// <summary>The generation features the configured endpoint supports.</summary>
    public required GenerationFeature Features { get; init; }

    /// <summary>
    /// The media transports the endpoint accepts for generation inputs, drawn
    /// from <see cref="LlmContentTransport"/>. An empty set claims the endpoint
    /// accepts no generation inputs (text-only generation).
    /// </summary>
    public IReadOnlySet<LlmContentTransport> InputTransports { get; init; } =
        new HashSet<LlmContentTransport>();

    /// <summary>
    /// The maximum number of candidates a single request may ask for, when the
    /// provider documents a limit.
    /// </summary>
    public int? MaximumCandidates { get; init; }

    /// <summary>Statically-known model/endpoint constraints, when documented.</summary>
    public GenerationConstraints? Constraints { get; init; }

    /// <summary>
    /// Returns whether every bit of <paramref name="feature"/> is advertised.
    /// </summary>
    /// <param name="feature">The feature (or combination) to test.</param>
    /// <returns><c>true</c> when the endpoint advertises the feature.</returns>
    public bool Supports(GenerationFeature feature) =>
        (Features & feature) == feature;
}
