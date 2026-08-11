using System.Diagnostics.Metrics;

namespace Penghou.Baize.Diagnostics;

internal static class DiagnosticsTelemetry
{
    public static Counter<long> Sessions { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.diagnostics.sessions");

    public static Counter<long> Failures { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.diagnostics.failures");

    public static Counter<long> TruncatedBodies { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.diagnostics.truncated_bodies");

    public static Counter<long> CapturedBytes { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.diagnostics.captured_bytes", "By");

    public static Histogram<double> Duration { get; } =
        BaizeTelemetry.Meter.CreateHistogram<double>(
            "baize.diagnostics.duration",
            "ms");
}
