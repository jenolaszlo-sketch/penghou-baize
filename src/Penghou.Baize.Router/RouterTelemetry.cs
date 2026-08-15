using System.Diagnostics.Metrics;

namespace Penghou.Baize.Router;

internal static class RouterTelemetry
{
    public static Counter<long> Attempts { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.router.attempts");

    public static Counter<long> Failures { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.router.failures");

    public static Counter<long> Fallbacks { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.router.fallbacks");

    public static Counter<long> Retries { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.router.retries");

    public static Histogram<double> AttemptDuration { get; } =
        BaizeTelemetry.Meter.CreateHistogram<double>(
            "baize.router.attempt.duration",
            "ms");

    public static Counter<long> EndpointValidations { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.endpoint.validations");

    public static Counter<long> ConfigurationReloads { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.router.configuration.reloads");

    public static Counter<long> ConfigurationReloadFailures { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>(
            "baize.router.configuration.reload_failures");

    public static Counter<long> ProviderModuleLoads { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>("baize.provider.module.loads");

    public static Counter<long> ProviderModuleLoadFailures { get; } =
        BaizeTelemetry.Meter.CreateCounter<long>(
            "baize.provider.module.load_failures");

    public static Histogram<double> ProviderModuleLoadDuration { get; } =
        BaizeTelemetry.Meter.CreateHistogram<double>(
            "baize.provider.module.load.duration",
            "ms");
}
