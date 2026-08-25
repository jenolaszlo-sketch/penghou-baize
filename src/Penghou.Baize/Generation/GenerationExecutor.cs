using Microsoft.Extensions.Options;

namespace Penghou.Baize.Generation;

/// <summary>
/// The default in-process <see cref="IGenerationExecutor"/>. It selects an
/// endpoint via the <see cref="IGenerationRoutingPolicy"/>, submits exactly
/// once, pins the returned handle, polls with backoff, reports progress,
/// enforces a timeout, and returns the terminal result.
/// </summary>
public sealed class GenerationExecutor : IGenerationExecutor
{
    private readonly GenerationExecutorCore _core;

    /// <summary>Initializes the executor.</summary>
    /// <param name="registry">The registry of registered generation endpoints.</param>
    /// <param name="routingPolicy">The routing policy, or the deterministic default when null.</param>
    /// <param name="options">The polling configuration, or defaults when null.</param>
    /// <param name="endpointOrderer">Optional shared reliability ordering applied before routing selection.</param>
    public GenerationExecutor(
        IGenerationClientRegistry registry,
        IGenerationRoutingPolicy? routingPolicy = null,
        IOptions<GenerationExecutorOptions>? options = null,
        IGenerationEndpointOrderer? endpointOrderer = null)
    {
        var effectiveOptions = options?.Value ?? new GenerationExecutorOptions();
        GenerationExecutorCore.ValidateOptions(effectiveOptions);
        _core = new GenerationExecutorCore(
            registry ?? throw new ArgumentNullException(nameof(registry)),
            routingPolicy ?? new DefaultGenerationRoutingPolicy(),
            effectiveOptions,
            endpointOrderer);
    }

    /// <inheritdoc />
    public async Task<GenerationResult> ExecuteAsync(
        GenerationRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = await _core
            .SelectEndpointAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var operation = await endpoint.Client
            .SubmitAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await _core.WaitForResultAsync(
                endpoint, operation, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<GenerationResult> WaitAsync(
        GenerationOperationHandle handle,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        _core.WaitAsync(handle, progress, cancellationToken);
}
