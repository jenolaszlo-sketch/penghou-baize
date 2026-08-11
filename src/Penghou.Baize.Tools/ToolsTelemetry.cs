using System.Diagnostics.Metrics;

namespace Penghou.Baize.Tools;

internal static class ToolsTelemetry
{
    public static Counter<long> RepairAttempts { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.json.repair.attempts");

    public static Counter<long> Repairs { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.json.repairs");

    public static Histogram<double> RepairDuration { get; } =
        BaizeTelemetry.Meter.CreateHistogram<double>(
            "baize.json.repair.duration",
            "ms");
}
