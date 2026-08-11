using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Penghou.Baize.IntegrationTests;

internal sealed class LiveTelemetryScope : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public LiveTelemetryScope(ITestOutputHelper output)
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == BaizeTelemetry.InstrumentationName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => output.WriteLine(
                "TRACE {0} status={1} duration_ms={2:F1} tags={3}",
                activity.OperationName,
                activity.Status,
                activity.Duration.TotalMilliseconds,
                string.Join(",", activity.Tags.Select(tag =>
                    $"{tag.Key}={tag.Value}")))
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BaizeTelemetry.InstrumentationName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => output.WriteLine(
                "METRIC {0}={1} tags={2}",
                instrument.Name,
                value,
                FormatTags(tags)));
        _meterListener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => output.WriteLine(
                "METRIC {0}={1:F2} tags={2}",
                instrument.Name,
                value,
                FormatTags(tags)));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(",", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
