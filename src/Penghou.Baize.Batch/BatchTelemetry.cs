using System.Diagnostics.Metrics;

namespace Penghou.Baize.Batch;

internal static class BatchTelemetry
{
    public static Counter<long> Submissions { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.batch.submissions");

    public static Counter<long> StatusPolls { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.batch.status.polls");

    public static Counter<long> TransientFailures { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.batch.transient_failures");

    public static Histogram<double> WaitDuration { get; } =
        BaizeTelemetry.Meter.CreateHistogram<double>("baize.batch.wait.duration", "ms");
}
