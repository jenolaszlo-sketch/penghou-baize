namespace Penghou.Baize.Router;

/// <summary>
/// Streams completions through a concrete endpoint, selecting the
/// least-failing endpoint for a model or strategy and recording per-endpoint
/// call and failure history.
/// </summary>
public interface ILlmRouter
{
    /// <summary>
    /// Streams a completion for a model, using the endpoint the router would
    /// currently pick for that model.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default);

    /// <summary>Streams an already-built canonical request through a named model.</summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        string model,
        LlmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a completion for a strategy, using the endpoint the router
    /// would currently pick from the strategy's fallback chain.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default);

    /// <summary>Streams an already-built canonical request through a strategy.</summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        ModelStrategy strategy,
        LlmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a request through an application-defined named fallback route.
    /// Named routes are distinct from model registration names.
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        ILlmPromptBuilder builder,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not support named routes.");

    /// <summary>Streams a canonical request through a named fallback route.</summary>
    IAsyncEnumerable<LlmStreamEvent> StreamRouteAsync(
        string route,
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not support named routes.");

    /// <summary>
    /// The endpoint the router would currently use for a model, chosen from
    /// the model's configured endpoints by least-failing history.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The resolved endpoint.</returns>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    ResolvedEndpoint Resolve(string model);

    /// <summary>Asynchronously resolves the endpoint currently preferred for a model.</summary>
    Task<ResolvedEndpoint> ResolveAsync(
        string model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The endpoint the router would currently use for a strategy, chosen
    /// from the fallback chain's endpoints by least-failing history.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The resolved endpoint.</returns>
    [Obsolete("Use ResolveAsync to avoid blocking asynchronous router memory.")]
    ResolvedEndpoint Resolve(ModelStrategy strategy);

    /// <summary>Asynchronously resolves the endpoint currently preferred for a strategy.</summary>
    Task<ResolvedEndpoint> ResolveAsync(
        ModelStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves the endpoint currently preferred by a named route.</summary>
    Task<ResolvedEndpoint> ResolveRouteAsync(
        string route,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not support named routes.");

    /// <summary>Explains direct-model selection without sending a request.</summary>
    Task<LlmRouteExplanation> ExplainModelAsync(
        string model,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not expose route explanations.");

    /// <summary>Explains typed-strategy selection without sending a request.</summary>
    Task<LlmRouteExplanation> ExplainStrategyAsync(
        ModelStrategy strategy,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not expose route explanations.");

    /// <summary>Explains named-route selection without sending a request.</summary>
    Task<LlmRouteExplanation> ExplainRouteAsync(
        string route,
        LlmRequest? request = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"Router '{GetType().FullName}' does not expose route explanations.");
}
