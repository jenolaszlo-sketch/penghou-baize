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

    /// <summary>
    /// The endpoint the router would currently use for a model, chosen from
    /// the model's configured endpoints by least-failing history.
    /// </summary>
    /// <param name="model">The model's registration name.</param>
    /// <returns>The resolved endpoint.</returns>
    ResolvedEndpoint Resolve(string model);

    /// <summary>
    /// The endpoint the router would currently use for a strategy, chosen
    /// from the fallback chain's endpoints by least-failing history.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The resolved endpoint.</returns>
    ResolvedEndpoint Resolve(ModelStrategy strategy);
}
