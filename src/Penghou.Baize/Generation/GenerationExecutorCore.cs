namespace Penghou.Baize.Generation;

/// <summary>
/// Shared mechanics for the single-request and batch executors: candidate
/// selection via non-throwing capability probing, the poll-to-terminal loop
/// with backoff and progress reporting, wait-by-handle resume, and the
/// normalized failure factories. Extracted so both executors stay behaviorally
/// identical as the generation stack evolves.
/// </summary>
internal sealed class GenerationExecutorCore(
    IGenerationClientRegistry registry,
    IGenerationRoutingPolicy routingPolicy,
    GenerationExecutorOptions options,
    IGenerationEndpointOrderer? endpointOrderer = null)
{
    public async Task<GenerationEndpoint> SelectEndpointAsync(
        GenerationRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GenerationEndpoint> candidates = registry.Endpoints
            .Where(endpoint => Supports(endpoint, request))
            .ToArray();

        if (endpointOrderer is not null && candidates.Count > 1)
        {
            candidates = await endpointOrderer
                .OrderAsync(candidates, cancellationToken)
                .ConfigureAwait(false);
        }

        return routingPolicy.Select(request, candidates)
            ?? throw new BaizeException(
                "No configured generation endpoint can satisfy the request.",
                GenerationErrorKind.InvalidRequest);
    }

    /// <summary>
    /// Resolves the endpoint pinned by an operation handle — never re-routing —
    /// and polls the operation to a terminal state.
    /// </summary>
    public async Task<GenerationResult> WaitAsync(
        GenerationOperationHandle handle,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);

        var endpoint = registry.Endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.EndpointId, handle.EndpointId, StringComparison.Ordinal) &&
            string.Equals(candidate.Provider, handle.Provider, StringComparison.Ordinal));

        if (endpoint is null)
        {
            throw BaizeException.InvalidRequest(
                $"No configured generation endpoint matches operation handle " +
                $"'{handle.Id}' (provider '{handle.Provider}', endpoint '{handle.EndpointId}').");
        }

        return await WaitForResultAsync(
                endpoint,
                new GenerationOperation(handle, GenerationOperationState.Running),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GenerationResult> WaitForResultAsync(
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
        var deadline = DateTimeOffset.UtcNow + options.Timeout;
        var interval = options.InitialPollingInterval;
        var multiplier = options.PollingBackoffMultiplier;

        while (true)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw CreateTimeout(handle, Describe(endpoint));

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            interval = TimeSpan.FromTicks(
                (long)Math.Min(
                    interval.Ticks * multiplier,
                    options.MaxPollingInterval.Ticks));

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

    public static bool Supports(GenerationEndpoint endpoint, GenerationRequest request) =>
        GenerationRequestValidator.TryValidate(
            endpoint.Client.Capabilities,
            request,
            out _);

    public static string Describe(GenerationEndpoint endpoint) =>
        $"{endpoint.Provider}/{endpoint.EndpointId}";

    private static GenerationResult RequireResult(GenerationOperation operation) =>
        operation.Result is { } result
            ? result
            : throw new BaizeException(
                $"Generation operation '{operation.Handle.Id}' succeeded but returned no assets.",
                GenerationErrorKind.GenerationFailed);

    /// <summary>Shared result-or-failure mapping used by batch chunk finalization.</summary>
    internal static GenerationResult RequireTerminalResult(GenerationOperation operation) =>
        RequireResult(operation);

    internal static BaizeException CreateFailure(GenerationOperation operation) =>
        operation.Error is { } error
            ? new BaizeException(
                error.Message ?? "Generation failed.",
                error.Kind,
                error.StatusCode,
                providerStatus: error.ProviderStatus)
            : new BaizeException(
                $"Generation operation '{operation.Handle.Id}' failed.",
                GenerationErrorKind.GenerationFailed);

    internal static BaizeException CreateCanceled(GenerationOperation operation) =>
        new(
            $"Generation operation '{operation.Handle.Id}' was canceled.",
            GenerationErrorKind.Canceled);

    private static BaizeException CreateTimeout(
        GenerationOperationHandle handle,
        string endpointDescription) =>
        new(
            $"Generation operation '{handle.Id}' on endpoint '{endpointDescription}' did not " +
            "complete within the configured timeout. It may still be running; resume it later " +
            "by calling WaitAsync with this handle.",
            GenerationErrorKind.TimeoutExceeded);

    public static void ValidateOptions(GenerationExecutorOptions options)
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
