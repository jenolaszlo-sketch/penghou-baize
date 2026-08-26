using System.Runtime.CompilerServices;
using System.Text;

namespace Penghou.Baize;

/// <summary>
/// Provider-neutral helpers for collecting canonical LLM event streams and
/// completing requests without requiring the router package.
/// </summary>
public static class LlmStreamingExtensions
{
    /// <summary>Completes a request without observing incremental deltas.</summary>
    public static Task<LlmResponse> CompleteAsync(
        this ILlmClient client,
        LlmRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync(client, request, null, cancellationToken);

    /// <summary>
    /// Completes a request, preferring a native non-streaming implementation
    /// when the client supplies one and otherwise collecting its stream.
    /// </summary>
    public static Task<LlmResponse> CompleteAsync(
        this ILlmClient client,
        LlmRequest request,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        if (onDelta is null && client is ILlmCompletionClient nativeClient)
            return nativeClient.CompleteAsync(request, cancellationToken);

        return client.StreamAsync(request, cancellationToken)
            .CollectAsync(onDelta, cancellationToken);
    }

    /// <summary>
    /// Collects a canonical event stream into a single response while
    /// preserving ordered content parts, tool calls, continuations, usage,
    /// provider diagnostics, router diagnostics, and repair diagnostics.
    /// </summary>
    public static async Task<LlmResponse> CollectAsync(
        this IAsyncEnumerable<LlmStreamEvent> stream,
        Action<string>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
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
        bool contentWasRepaired = false;
        IReadOnlyList<LlmRepairAttempt>? contentRepairAttempts = null;
        LlmJsonRepairDiagnostics? contentRepairDiagnostics = null;
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

        static void AttachContinuation(
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

        await foreach (var evt in stream
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
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
                // which streams after the thinking text) belongs to the
                // reasoning block it follows.
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

            if (evt.ContentWasRepaired)
                contentWasRepaired = true;

            if (evt.ContentRepairAttempts is not null)
                contentRepairAttempts = evt.ContentRepairAttempts;

            if (evt.ContentRepairDiagnostics is not null)
                contentRepairDiagnostics = evt.ContentRepairDiagnostics;
        }

        foreach (var (index, builder) in toolCallBuilders)
        {
            if (builder.Name is null)
            {
                throw new LlmClientException(
                    $"Stream ended with incomplete tool call {index}: no name " +
                    $"was received and {builder.Arguments.Length} argument " +
                    "character(s) remain buffered.",
                    LlmClientFailureKind.Protocol);
            }
        }

        var toolCalls = toolCallBuilders.Values
            .Select(builder => builder.Materialize())
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
            ContentWasRepaired: contentWasRepaired,
            ContentRepairAttempts: contentRepairAttempts,
            ReasoningContinuation: reasoningContinuation,
            ContentContinuation: contentContinuation)
        {
            Parts = MaterializeParts(),
            ContentRepairDiagnostics = contentRepairDiagnostics
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
