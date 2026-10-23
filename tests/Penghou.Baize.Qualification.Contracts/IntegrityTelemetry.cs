using System.Diagnostics;

namespace Penghou.Baize.Qualification;

internal sealed record IntegrityObservation(
    bool? Succeeded,
    int? NormalizedCharacters,
    int? EmittedCharacters,
    int? ConsumedCharacters,
    int? BufferedCharacters,
    int? WarningCount,
    int? ToolCallCount);

// Reads the existing public Activity events; does not expose core internals,
// install HTTP capture, or retain arbitrary tags. Each invocation has its own trace.
internal sealed class IntegrityTelemetry : IDisposable
{
    private readonly Activity _scope;
    private readonly ActivityListener _listener;
    private readonly List<IntegrityObservation> _observations = [];
    private readonly object _gate = new();

    public IntegrityTelemetry()
    {
        var traceId = ActivityTraceId.CreateRandom();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BaizeTelemetry.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.TraceId != traceId)
                    return;
                foreach (var evt in activity.Events.Where(e => e.Name == "StreamCompleted"))
                {
                    var tags = evt.Tags.ToDictionary(p => p.Key, p => p.Value);
                    int? Count(string name) => tags.GetValueOrDefault("baize.stream." + name) is int n && n >= 0 ? n : null;
                    var observation = new IntegrityObservation(
                        tags.GetValueOrDefault("baize.stream.succeeded") as bool?,
                        Count("normalized_character_count"), Count("emitted_character_count"),
                        Count("consumed_protocol_character_count"), Count("buffered_character_count"),
                        Count("protocol_warning_count"), Count("tool_call_count"));
                    lock (_gate)
                        _observations.Add(observation);
                }
            }
        };
        ActivitySource.AddActivityListener(_listener);
        _scope = new Activity("BaizeQualification")
            .SetParentId(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)
            .Start();
    }

    public IReadOnlyList<IntegrityObservation> Snapshot()
    {
        lock (_gate)
            return Array.AsReadOnly(_observations.ToArray());
    }

    public void Dispose()
    {
        _scope.Dispose();
        _listener.Dispose();
    }
}
