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
        var orderedParts = new List<PartBuilder>();
        var indexedParts = new Dictionary<int, PartBuilder>();
        PartBuilder? fallbackPart = null;
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        LlmProviderContinuation? reasoningContinuation = null;
        LlmProviderContinuation? contentContinuation = null;
        LlmUsage? usage = null;
        LlmProviderDiagnostics? diagnostics = null;
        LlmRouterDiagnostics? routerDiagnostics = null;
        string? finishReason = null;
        var toolCallBuilders = new SortedDictionary<int, ToolCallBuilder>();

        PartBuilder GetPart(int? index, PartKind kind)
        {
            if (index is { } partIndex)
            {
                if (indexedParts.TryGetValue(partIndex, out var existing))
                {
                    if (existing.Kind != kind)
                    {
                        throw new LlmClientException(
                            $"Stream part {partIndex} changed from " +
                            $"{existing.Kind} to {kind}.",
                            LlmClientFailureKind.Protocol);
                    }

                    return existing;
                }

                var indexed = new PartBuilder(kind);
                indexedParts[partIndex] = indexed;
                orderedParts.Add(indexed);
                fallbackPart = null;
                return indexed;
            }

            if (fallbackPart is not null && fallbackPart.Kind == kind)
                return fallbackPart;

            fallbackPart = new PartBuilder(kind);
            orderedParts.Add(fallbackPart);
            return fallbackPart;
        }

        void AttachContinuation(
            PartBuilder part,
            LlmProviderContinuation? continuation)
        {
            if (continuation is not null)
                part.Continuation = continuation;
        }

        ToolCallBuilder GetToolCall(ToolCallDelta delta, int? partIndex)
        {
            if (toolCallBuilders.TryGetValue(delta.Index, out var existing))
                return existing;

            var part = GetPart(partIndex, PartKind.ToolCall);

            if (part.ToolCall is not null)
            {
                throw new LlmClientException(
                    $"Stream part {partIndex} contains more than one tool call.",
                    LlmClientFailureKind.Protocol);
            }

            var created = new ToolCallBuilder();
            part.ToolCall = created;
            toolCallBuilders[delta.Index] = created;
            return created;
        }

        IReadOnlyList<LlmContentPart> MaterializeParts()
        {
            var result = new List<LlmContentPart>(orderedParts.Count);

            foreach (var part in orderedParts)
            {
                switch (part.Kind)
                {
                    case PartKind.Text:
                        result.Add(new LlmTextContent(part.Text.ToString())
                        {
                            Continuation = part.Continuation
                        });
                        break;

                    case PartKind.Reasoning:
                        // Empty reasoning is significant: Claude can stream a
                        // signature-only thinking block when display is omitted.
                        result.Add(new LlmReasoningContent(part.Text.ToString())
                        {
                            Continuation = part.Continuation
                        });
                        break;

                    case PartKind.ToolCall when part.ToolCall?.Name is not null:
                        result.Add(new LlmToolCallContent(
                            part.ToolCall.Materialized!));
                        break;
                }
            }

            return result;
        }

        await foreach (var evt in stream)
        {
            if (evt.Delta is not null)
            {
                content.Append(evt.Delta);
                onDelta?.Invoke(evt.Delta);

                if (evt.Continuation is not null)
                    contentContinuation = evt.Continuation;

                var part = GetPart(evt.PartIndex, PartKind.Text);
                part.Text.Append(evt.Delta);
                AttachContinuation(part, evt.Continuation);
            }

            if (evt.ReasoningContent is not null)
            {
                reasoning.Append(evt.ReasoningContent);

                if (evt.Continuation is not null)
                    reasoningContinuation = evt.Continuation;

                var part = GetPart(evt.PartIndex, PartKind.Reasoning);
                part.Text.Append(evt.ReasoningContent);
                AttachContinuation(part, evt.Continuation);
            }
            else if (evt.Continuation is not null &&
                     evt.Delta is null &&
                     evt.ToolCallDelta is null)
            {
                // A bare continuation (for example Claude's signature_delta,
                // which streams in its own event after the thinking text)
                // still belongs to the reasoning block it follows.
                reasoningContinuation = evt.Continuation;

                var part = evt.PartIndex is { } partIndex &&
                           indexedParts.TryGetValue(partIndex, out var indexed)
                    ? indexed
                    : GetPart(evt.PartIndex, PartKind.Reasoning);
                AttachContinuation(part, evt.Continuation);
            }

            if (evt.ToolCallDelta is { } toolDelta)
            {
                var builder = GetToolCall(toolDelta, evt.PartIndex);

                if (toolDelta.Id is not null) builder.Id = toolDelta.Id;
                if (toolDelta.Name is not null) builder.Name = toolDelta.Name;
                if (toolDelta.ArgumentsJsonFragment is not null)
                    builder.Arguments.Append(toolDelta.ArgumentsJsonFragment);

                if (toolDelta.Continuation is not null)
                    builder.Continuation = toolDelta.Continuation;
                else if (evt.Continuation is not null)
                    builder.Continuation = evt.Continuation;
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
            .Select(b => b.Materialize())
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
            ReasoningContinuation: reasoningContinuation,
            ContentContinuation: contentContinuation)
        {
            Parts = MaterializeParts()
        };
    }

    private enum PartKind
    {
        Text,
        Reasoning,
        ToolCall
    }

    private sealed class PartBuilder(PartKind kind)
    {
        public PartKind Kind { get; } = kind;
        public StringBuilder Text { get; } = new();
        public ToolCallBuilder? ToolCall { get; set; }
        public LlmProviderContinuation? Continuation { get; set; }
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
        public LlmProviderContinuation? Continuation { get; set; }
        public LlmToolCall? Materialized { get; private set; }

        public LlmToolCall Materialize() =>
            Materialized ??= new LlmToolCall(
                Id: Id ?? Guid.NewGuid().ToString(),
                Name: Name!,
                ArgumentsJson: Arguments.ToString(),
                Continuation: Continuation);
    }
}
