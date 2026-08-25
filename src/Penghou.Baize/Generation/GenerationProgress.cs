namespace Penghou.Baize.Generation;

/// <summary>A provider-neutral progress snapshot for queued generation.</summary>
/// <param name="Fraction">Completion fraction in the range 0.0–1.0, when known.</param>
/// <param name="Phase">Provider-neutral phase or lifecycle state, when known.</param>
/// <param name="QueuePosition">One-based queue position, when reported.</param>
public sealed record GenerationProgress(
    double? Fraction = null,
    string? Phase = null,
    int? QueuePosition = null);
