namespace Penghou.Baize.Router;

/// <summary>
/// Convenience helpers that complete routed requests by using the canonical
/// stream collector from <c>Penghou.Baize</c>.
/// </summary>
public static class LlmRouterStreamingExtensions
{
    /// <summary>Streams a completion for a model and collects it into a response.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        string model,
        ILlmPromptBuilder builder,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamAsync(model, builder, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);

    /// <summary>Streams a canonical request for a model and collects it.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        string model,
        LlmRequest request,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamAsync(model, request, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);

    /// <summary>Streams a canonical request for a model and collects it.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        string model,
        LlmRequest request,
        CancellationToken cancellationToken) =>
        router.StreamAsync(model, request, cancellationToken)
            .CollectAsync(cancellationToken: cancellationToken);

    /// <summary>Streams a completion for a strategy and collects it.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamAsync(strategy, builder, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);

    /// <summary>Streams a canonical request for a strategy and collects it.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        ModelStrategy strategy,
        LlmRequest request,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamAsync(strategy, request, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);

    /// <summary>Streams a canonical request for a strategy and collects it.</summary>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        ModelStrategy strategy,
        LlmRequest request,
        CancellationToken cancellationToken) =>
        router.StreamAsync(strategy, request, cancellationToken)
            .CollectAsync(cancellationToken: cancellationToken);

    /// <summary>Streams a canonical request through a named route and collects it.</summary>
    public static Task<LlmResponse> CompleteRouteAsync(
        this ILlmRouter router,
        string route,
        LlmRequest request,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamRouteAsync(route, request, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);

    /// <summary>Builds, streams, and collects a request through a named route.</summary>
    public static Task<LlmResponse> CompleteRouteAsync(
        this ILlmRouter router,
        string route,
        ILlmPromptBuilder builder,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        router.StreamRouteAsync(route, builder, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);
}
