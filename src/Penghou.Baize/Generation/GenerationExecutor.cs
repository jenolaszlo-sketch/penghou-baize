using Microsoft.Extensions.Options;

namespace Penghou.Baize.Generation;

/// <summary>
/// The default in-process <see cref="IGenerationExecutor"/>. It routes once,
/// submits once, pins the returned handle, polls with backoff, reports provider
/// progress, enforces a timeout, and returns the terminal result. Submission
/// failures are surfaced, never replayed; only status reads are retried with
/// backoff because they are safe and never create a duplicate billable job.
/// </summary>
public sealed class GenerationExecutor : IGenerationExecutor
{
    private readonly IGenerationClientRegistry _registry;
    private readonly IGenerationRoutingPolicy _routingPolicy;
    private readonly GenerationExecutorOptions _options;

    /// <summary>Initializes the executor.</summary>
    /// <param name="registry">The registry of registered generation endpoints.</param>
    /// <param name="routingPolicy">The routing policy, or the deterministic default when null.</param>
    /// <param name="options">The polling configuration, or defaults when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public GenerationExecutor(
        IGenerationClientRegistry registry,
        IGenerationRoutingPolicy? routingPolicy = null,
        IOptions<GenerationExecutorOptions>? options = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _routingPolicy = routingPolicy ?? new DefaultGenerationRoutingPolicy();
        _options = options?.Value ?? new GenerationExecutorOptions();
        ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<GenerationResult> ExecuteAsync(
        GenerationRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = SelectEndpoint(request);
        var operation = await endpoint.Client
            .SubmitAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await WaitForResultAsync(
                endpoint, operation, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private GenerationEndpoint SelectEndpoint(GenerationRequest request)
    {
        var candidates = _registry.Endpoints
            .Where(endpoint => Supports(endpoint, request))
            .ToArray();

        return _routingPolicy.Select(request, candidates)
            ?? throw new BaizeException(
                "No configured generation endpoint can satisfy the request.",
                GenerationErrorKind.InvalidRequest);
    }

    private static bool Supports(GenerationEndpoint endpoint, GenerationRequest request)
    {
        try
        {
            GenerationRequestValidator.Validate(
                endpoint.Client.Capabilities,
                request,
                Describe(endpoint));
            return true;
        }
        catch (BaizeException)
        {
            return false;
        }
    }

    private static string Describe(GenerationEndpoint endpoint) =>
        $"{endpoint.Provider}/{endpoint.EndpointId}";

    private async Task<GenerationResult> WaitForResultAsync(
        GenerationEndpoint endpoint,
        GenerationOperation operation,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        switch (operation.State)
        {
            case GenerationOperationState.Succeeded:
                return RequireResult(operation);
            case GenerationOperationState.Failed:
                throw CreateFailure(operation);
            case GenerationOperationState.Canceled:
                throw CreateCanceled(operation);
        }

        var handle = operation.Handle;
        var deadline = DateTimeOffset.UtcNow + _options.Timeout;
        var interval = _options.InitialPollingInterval;
        var multiplier = _options.PollingBackoffMultiplier;

        while (true)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw CreateTimeout(handle, endpoint);

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            interval = TimeSpan.FromTicks(
                (long)Math.Min(
                    interval.Ticks * multiplier,
                    _options.MaxPollingInterval.Ticks));

            GenerationOperation snapshot;
            try
            {
                snapshot = await endpoint.Client
                    .GetAsync(handle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (BaizeException exception) when (
                exception.ErrorKind is GenerationErrorKind.ProviderUnavailable
                    or GenerationErrorKind.RateLimited)
            {
                continue;
            }

            if (snapshot.Progress is { } value)
                progress?.Report(Math.Clamp(value, 0.0, 1.0));

            switch (snapshot.State)
            {
                case GenerationOperationState.Succeeded:
                    return RequireResult(snapshot);
                case GenerationOperationState.Failed:
                    throw CreateFailure(snapshot);
                case GenerationOperationState.Canceled:
                    throw CreateCanceled(snapshot);
            }
        }
    }

    private static GenerationResult RequireResult(GenerationOperation operation) =>
        operation.Result is { } result
            ? result
            : throw new BaizeException(
                $"Generation operation '{operation.Handle.Id}' succeeded but returned no assets.",
                GenerationErrorKind.GenerationFailed);

    private static Exception CreateFailure(GenerationOperation operation) =>
        operation.Error is { } error
            ? new BaizeException(
                error.Message ?? "Generation failed.",
                error.Kind,
                error.StatusCode,
                providerStatus: error.ProviderStatus)
            : new BaizeException(
                $"Generation operation '{operation.Handle.Id}' failed.",
                GenerationErrorKind.GenerationFailed);

    private static Exception CreateCanceled(GenerationOperation operation) =>
        new BaizeException(
            $"Generation operation '{operation.Handle.Id}' was canceled.",
            GenerationErrorKind.Canceled);

    private static Exception CreateTimeout(
        GenerationOperationHandle handle,
        GenerationEndpoint endpoint) =>
        new BaizeException(
            $"Generation operation '{handle.Id}' on endpoint " +
            $"'{Describe(endpoint)}' did not complete within the configured " +
            "timeout. It may still be running; resume it later with this handle.",
            GenerationErrorKind.TimeoutExceeded);

    private static void ValidateOptions(GenerationExecutorOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Timeout must be positive.");
        if (options.InitialPollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), "InitialPollingInterval must be positive.");
        if (options.MaxPollingInterval < options.InitialPollingInterval)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxPollingInterval must be at least InitialPollingInterval.");
        if (options.PollingBackoffMultiplier < 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(options), "PollingBackoffMultiplier must be at least 1.0.");
    }
}
