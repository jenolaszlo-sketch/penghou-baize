using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Penghou.Baize;

/// <summary>Standard diagnostics emitted by Baize clients and routers.</summary>
public static class BaizeTelemetry
{
    /// <summary>The activity and meter instrumentation name.</summary>
    public const string InstrumentationName = "Penghou.Baize";

    /// <summary>Activities for provider calls and routed attempts.</summary>
    public static ActivitySource Activities { get; } =
        new(InstrumentationName);

    /// <summary>Metrics for calls, failures, latency, and tokens.</summary>
    public static Meter Meter { get; } = new(InstrumentationName);

    internal static Counter<long> Requests { get; } =
        Meter.CreateCounter<long>("baize.llm.requests");

    internal static Counter<long> Failures { get; } =
        Meter.CreateCounter<long>("baize.llm.failures");

    internal static Counter<long> InputTokens { get; } =
        Meter.CreateCounter<long>("baize.llm.input_tokens");

    internal static Counter<long> OutputTokens { get; } =
        Meter.CreateCounter<long>("baize.llm.output_tokens");

    internal static Histogram<double> Duration { get; } =
        Meter.CreateHistogram<double>("baize.llm.duration", "ms");
}
