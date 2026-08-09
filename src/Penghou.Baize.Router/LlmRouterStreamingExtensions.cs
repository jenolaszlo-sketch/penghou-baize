using System.Text;

namespace Penghou.Baize.Router;

/// <summary>
/// Convenience helpers that drain a routed stream into a single
/// <see cref="LlmResponse"/>.
/// </summary>
public static class LlmRouterStreamingExtensions
{
    /// <summary>Streams a completion for a model and collects it into a response.</summary>
    /// <param name="router">The router to stream through.</param>
    /// <param name="model">The model's registration name.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="onDelta">An optional callback invoked with each content fragment.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The collected response.</returns>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        string model,
        ILlmPromptBuilder builder,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default)
        => CollectAsync(router.StreamAsync(model, builder, cancellationToken), onDelta);

    /// <summary>Streams a completion for a strategy and collects it into a response.</summary>
    /// <param name="router">The router to stream through.</param>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <param name="builder">Builds the request for the stream.</param>
    /// <param name="onDelta">An optional callback invoked with each content fragment.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The collected response.</returns>
    public static Task<LlmResponse> CompleteStreamingAsync(
        this ILlmRouter router,
        ModelStrategy strategy,
        ILlmPromptBuilder builder,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default)
        => CollectAsync(router.StreamAsync(strategy, builder, cancellationToken), onDelta);

    private static async Task<LlmResponse> CollectAsync(
        IAsyncEnumerable<LlmStreamEvent> stream,
        Action<string>? onDelta)
    {
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        LlmProviderContinuation? reasoningContinuation = null;
        LlmUsage? usage = null;
        LlmProviderDiagnostics? diagnostics = null;
        LlmRouterDiagnostics? routerDiagnostics = null;
        string? finishReason = null;
        var toolCallBuilders = new SortedDictionary<int, ToolCallBuilder>();

        await foreach (var evt in stream)
        {
            if (evt.Delta is not null)
            {
                content.Append(evt.Delta);
                onDelta?.Invoke(evt.Delta);
            }

            if (evt.ReasoningContent is not null)
            {
                reasoning.Append(evt.ReasoningContent);

                if (evt.Continuation is not null)
                    reasoningContinuation = evt.Continuation;
            }

            if (evt.ToolCallDelta is { } toolDelta)
            {
                if (!toolCallBuilders.TryGetValue(toolDelta.Index, out var builder))
                {
                    builder = new ToolCallBuilder();
                    toolCallBuilders[toolDelta.Index] = builder;
                }

                if (toolDelta.Id is not null) builder.Id = toolDelta.Id;
                if (toolDelta.Name is not null) builder.Name = toolDelta.Name;
                if (toolDelta.ArgumentsJsonFragment is not null)
                    builder.Arguments.Append(toolDelta.ArgumentsJsonFragment);
            }

            if (evt.FinishReason is not null)
                finishReason = evt.FinishReason;

            if (evt.Usage is not null)
                usage = evt.Usage;

            if (evt.Diagnostics is not null)
                diagnostics = evt.Diagnostics;

            if (evt.RouterDiagnostics is not null)
                routerDiagnostics = evt.RouterDiagnostics;
        }

        var toolCalls = toolCallBuilders.Values
            .Where(b => b.Name is not null)
            .Select(b => new LlmToolCall(
                Id: b.Id ?? Guid.NewGuid().ToString(),
                Name: b.Name!,
                ArgumentsJson: b.Arguments.ToString()))
            .ToList();

        return new LlmResponse(
            Content: content.ToString(),
            Reasoning: reasoning.Length == 0
                ? null
                : reasoning.ToString(),
            FinishReason: finishReason,
            Usage: usage,
            ToolCalls: toolCalls,
            Diagnostics: diagnostics,
            RouterDiagnostics: routerDiagnostics,
            ReasoningContinuation: reasoningContinuation);
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
