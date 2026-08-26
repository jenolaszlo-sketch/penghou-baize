namespace Penghou.Baize;

/// <summary>
/// Audits normalized provider deltas, preserves their canonical ordering, and
/// owns any lookahead used to recognize protocol markers.
/// </summary>
internal sealed class LlmStreamAssembler
{
    private readonly StreamMarkerLookahead? _lookahead;
    private readonly Dictionary<int, ToolCallState> _toolCalls = [];
    private readonly List<StreamProtocolWarning> _warnings = [];
    private int _providerChunkCount;
    private int _providerCharacterCount;
    private int _normalizedCharacterCount;
    private int _emittedCharacterCount;
    private int _consumedProtocolCharacterCount;
    private string? _finishReason;
    private bool _completed;

    public LlmStreamAssembler(IEnumerable<string>? protocolMarkers = null)
    {
        if (protocolMarkers is not null)
            _lookahead = new StreamMarkerLookahead(protocolMarkers);
    }

    public IReadOnlyList<LlmStreamEvent> Accept(NormalizedStreamDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (_completed)
            throw new InvalidOperationException("The stream assembler is complete.");
        if (delta.ProviderCharacterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(delta));
        if (delta.ProviderChunkCount < 0)
            throw new ArgumentOutOfRangeException(nameof(delta));

        _providerChunkCount += delta.ProviderChunkCount;
        _providerCharacterCount += delta.ProviderCharacterCount;
        _normalizedCharacterCount += delta.NormalizedCharacterCount;
        if (delta.Event is not { } streamEvent)
            return [];

        _finishReason = streamEvent.FinishReason ?? _finishReason;
        ObserveToolCall(streamEvent.ToolCallDelta);

        if (_lookahead is null || streamEvent.Delta is null)
        {
            _emittedCharacterCount += delta.NormalizedCharacterCount;
            return [streamEvent];
        }

        var result = new List<LlmStreamEvent>();
        var segments = _lookahead.Append(streamEvent.Delta);
        var attachedMetadata = false;
        foreach (var segment in segments)
        {
            if (segment.IsProtocolMarker)
            {
                _consumedProtocolCharacterCount += segment.Value.Length;
                continue;
            }

            var emitted = attachedMetadata
                ? new LlmStreamEvent(Delta: segment.Value)
                {
                    PartIndex = streamEvent.PartIndex
                }
                : streamEvent with { Delta = segment.Value };
            result.Add(emitted);
            attachedMetadata = true;
            _emittedCharacterCount += segment.Value.Length;
        }

        _emittedCharacterCount +=
            (streamEvent.ReasoningContent?.Length ?? 0) +
            (streamEvent.ToolCallDelta?.ArgumentsJsonFragment?.Length ?? 0);

        if (!attachedMetadata && HasNonContentPayload(streamEvent))
            result.Add(streamEvent with { Delta = null });

        return result;
    }

    public StreamAssemblyCompletion Complete(StreamTerminalSignal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (_completed)
            throw new InvalidOperationException("The stream assembler is complete.");

        _completed = true;
        _finishReason = terminal.FinishReason ?? _finishReason;
        var events = new List<LlmStreamEvent>();
        if (_lookahead is not null)
        {
            foreach (var segment in _lookahead.Complete())
            {
                events.Add(new LlmStreamEvent(Delta: segment.Value));
                _emittedCharacterCount += segment.Value.Length;
            }
        }

        LlmClientException? error = ValidateToolCalls();
        if (!terminal.ProtocolCompleted)
        {
            _warnings.Add(new(
                "stream.terminal.incomplete",
                $"Provider stream ended with {terminal.Kind} without a complete protocol terminal."));
            error ??= new LlmClientException(
                "Provider stream ended without a complete terminal signal.",
                LlmClientFailureKind.Availability);
        }

        var diagnostics = Snapshot();
        if (!diagnostics.IsConserved)
        {
            _warnings.Add(new(
                "stream.integrity.mismatch",
                "Normalized characters do not equal emitted, consumed, and buffered characters."));
            diagnostics = Snapshot();
            error ??= new LlmClientException(
                "Stream character accounting invariant failed.",
                LlmClientFailureKind.Protocol);
        }

        return new(events, diagnostics, error);
    }

    public StreamIntegritySnapshot Snapshot() => new(
        _providerChunkCount,
        _providerCharacterCount,
        _normalizedCharacterCount,
        _emittedCharacterCount,
        _consumedProtocolCharacterCount,
        _lookahead?.BufferedCharacterCount ?? 0,
        _finishReason,
        _toolCalls.Count,
        _warnings.ToArray());

    private void ObserveToolCall(ToolCallDelta? delta)
    {
        if (delta is null)
            return;

        if (!_toolCalls.TryGetValue(delta.Index, out var state))
        {
            state = new ToolCallState();
            _toolCalls.Add(delta.Index, state);
        }

        if (delta.Name is { } name)
        {
            if (state.Name is not null &&
                !string.Equals(state.Name, name, StringComparison.Ordinal))
            {
                state.HasConflict = true;
            }

            state.Name = name;
        }

        if (delta.Id is { } id)
        {
            if (state.Id is not null &&
                !string.Equals(state.Id, id, StringComparison.Ordinal))
            {
                state.HasConflict = true;
            }

            state.Id = id;
        }
    }

    private LlmClientException? ValidateToolCalls()
    {
        var invalid = false;
        foreach (var (index, state) in _toolCalls.OrderBy(pair => pair.Key))
        {
            if (string.IsNullOrEmpty(state.Name))
            {
                _warnings.Add(new(
                    "stream.tool-call.name-missing",
                    $"Tool call {index} completed without a name."));
                invalid = true;
            }

            if (state.HasConflict)
            {
                _warnings.Add(new(
                    "stream.tool-call.identity-conflict",
                    $"Tool call {index} changed its identity while streaming."));
                invalid = true;
            }
        }

        return invalid
            ? new LlmClientException(
                "The stream ended with one or more incomplete or inconsistent tool calls.",
                LlmClientFailureKind.Protocol)
            : null;
    }

    private static bool HasNonContentPayload(LlmStreamEvent value) =>
        value.ReasoningContent is not null ||
        value.FinishReason is not null ||
        value.Usage is not null ||
        value.ToolCallDelta is not null ||
        value.Diagnostics is not null ||
        value.RateLimit is not null ||
        value.RouterDiagnostics is not null ||
        value.Continuation is not null ||
        value.ContentWasRepaired ||
        value.ContentRepairAttempts is not null ||
        value.ContentRepairDiagnostics is not null;

    private sealed class ToolCallState
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool HasConflict { get; set; }
    }
}
