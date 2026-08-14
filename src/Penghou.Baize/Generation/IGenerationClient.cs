namespace Penghou.Baize.Generation;

/// <summary>
/// The provider-neutral artifact-generation client. One instance represents one
/// configured Baize endpoint (base address + model + credentials + capabilities).
/// It models both immediate and asynchronous providers through a single
/// operation lifecycle; durable polling and orchestration belong above it in an
/// executor, not here.
/// </summary>
public interface IGenerationClient
{
    /// <summary>The capabilities of the configured endpoint/model.</summary>
    GenerationCapabilities Capabilities { get; }

    /// <summary>
    /// Submits a generation request. May return an immediate
    /// <see cref="GenerationOperationState.Succeeded"/> result or a queued
    /// operation handle depending on the provider. Because a submission is
    /// potentially billable, failures with ambiguous outcomes throw a
    /// <see cref="BaizeException"/> carrying
    /// <see cref="GenerationErrorKind.UnknownSubmissionOutcome"/> and are never
    /// replayed automatically.
    /// </summary>
    /// <param name="request">The modality-specific generation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The submitted operation snapshot.</returns>
    Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current state of an accepted operation using the endpoint
    /// pinned in the handle. Providers without
    /// <see cref="GenerationFeature.OperationRetrieval"/> throw a
    /// <see cref="BaizeException"/> with
    /// <see cref="GenerationErrorKind.UnsupportedCapability"/>.
    /// </summary>
    /// <param name="handle">The pinned operation identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation snapshot.</returns>
    Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an accepted operation on the provider using the endpoint pinned
    /// in the handle. Local cancellation of the waiting token is never
    /// interpreted as a provider-side cancellation; only this method invokes the
    /// provider's cancellation endpoint. Providers without
    /// <see cref="GenerationFeature.Cancellation"/> throw a
    /// <see cref="BaizeException"/> with
    /// <see cref="GenerationErrorKind.UnsupportedCapability"/>.
    /// </summary>
    /// <param name="handle">The pinned operation identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation snapshot after cancellation.</returns>
    Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default);
}