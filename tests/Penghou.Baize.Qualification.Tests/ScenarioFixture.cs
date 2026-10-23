using System.Diagnostics;
using System.Runtime.CompilerServices;
using Penghou.Baize.Qualification;

namespace Penghou.Baize.Qualification.Tests;

internal sealed class ScenarioFixture(params LlmStreamEvent[] events)
{
    private static readonly ActivitySource Source = new(BaizeTelemetry.InstrumentationName);
    public int Calls { get; private set; }
    public bool Disposed { get; private set; }
    public LlmRequest? Request { get; private set; }
    public Exception? Error { get; init; }
    public Action? BeforeCompletion { get; init; }
    public bool EmitTelemetry { get; init; } = true;
    public int TelemetryCopies { get; init; } = 1;
    public int BufferedCharacters { get; init; }
    public int WarningCount { get; init; }
    public int AccountingOffset { get; init; }
    public bool? Succeeded { get; init; } = true;

    public QualificationContext Context(bool tools = false) => new(StreamAsync, tools);

    private async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request,
        [EnumeratorCancellation] CancellationToken token)
    {
        Calls++;
        Request = request;
        using var activity = Source.StartActivity("ControlledFixture");
        try
        {
            foreach (var evt in events)
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                yield return evt;
            }
            BeforeCompletion?.Invoke();
            token.ThrowIfCancellationRequested();
            if (Error is not null)
                throw Error;
        }
        finally
        {
            Disposed = true;
            if (EmitTelemetry)
            {
                var characters = events.Sum(e => (e.Delta?.Length ?? 0) + (e.ReasoningContent?.Length ?? 0) +
                    (e.ToolCallDelta?.ArgumentsJsonFragment?.Length ?? 0));
                for (var i = 0; i < TelemetryCopies; i++)
                    activity?.AddEvent(new ActivityEvent("StreamCompleted", tags: new ActivityTagsCollection
                    {
                        ["baize.stream.succeeded"] = Succeeded,
                        ["baize.stream.normalized_character_count"] = characters + BufferedCharacters + AccountingOffset,
                        ["baize.stream.emitted_character_count"] = characters,
                        ["baize.stream.consumed_protocol_character_count"] = 0,
                        ["baize.stream.buffered_character_count"] = BufferedCharacters,
                        ["baize.stream.protocol_warning_count"] = WarningCount,
                        ["baize.stream.tool_call_count"] = events.Where(e => e.ToolCallDelta is not null).Select(e => e.ToolCallDelta!.Index).Distinct().Count()
                    }));
            }
        }
    }
}
