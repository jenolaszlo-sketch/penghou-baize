using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Penghou.Baize.Qualification.Planning;

// The reviewed transport profile is runner-owned, not a production Baize
// setting. It names one exact handler configuration and the physical
// HTTP-request upper bound established for it. Any material change to these
// fields changes the profile identity in the frozen plan and invalidates live
// eligibility; the profile is never inferred from a provider or endpoint.
internal sealed record TransportHandlerSettings(
    bool AllowAutoRedirect,
    bool UseCookies,
    bool UseProxy,
    string ProxyMode,
    string AutomaticDecompression,
    bool AutomaticAuthentication,
    bool PreAuthenticate,
    string CredentialsMode,
    bool RetryMiddleware,
    int MaxConnectionsPerServer,
    string ConnectTimeout,
    string PooledConnectionIdleTimeout,
    string PooledConnectionLifetime,
    bool EnableMultipleHttp2Connections,
    int MaxResponseHeadersKilobytes,
    string ExpectContinuePolicy,
    string TlsMode);

internal sealed record TransportProfileSnapshot(
    string ProfileId,
    string Revision,
    string TransportClass,
    string State,
    string Handler,
    string ProtocolVersion,
    string ProtocolVersionPolicy,
    TransportHandlerSettings? HandlerSettings,
    string? RuntimeFramework,
    string? RuntimeVersion,
    string? HttpAssemblyVersion,
    string? HttpAssemblyInformationalVersion,
    string? HttpAssemblyHash,
    int? PhysicalAttemptUpperBound,
    string? PhysicalAttemptBoundBasis,
    string[] UnsupportedProtocolVersions,
    string[] EvidenceRefs,
    string Identity);

internal sealed record TransportProfileDefinition(
    string Id,
    string Revision,
    string Handler,
    string ProtocolVersion,
    string ProtocolVersionPolicy,
    TransportHandlerSettings Settings,
    int PhysicalAttemptUpperBound,
    string PhysicalAttemptBoundBasis,
    string[] UnsupportedProtocolVersions,
    string[] EvidenceRefs)
{
    public SocketsHttpHandler CreateHandler()
    {
        if (PhysicalAttemptUpperBound != 1 || Settings.UseProxy || Settings.AllowAutoRedirect
            || Settings.UseCookies || Settings.AutomaticAuthentication || Settings.PreAuthenticate
            || Settings.RetryMiddleware)
            throw new InvalidOperationException("The reviewed transport profile is not bounded.");
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = Settings.AllowAutoRedirect,
            UseCookies = Settings.UseCookies,
            UseProxy = Settings.UseProxy,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.None,
            Credentials = null,
            DefaultProxyCredentials = null,
            PreAuthenticate = Settings.PreAuthenticate,
            MaxConnectionsPerServer = Settings.MaxConnectionsPerServer,
            ConnectTimeout = TimeSpan.Parse(Settings.ConnectTimeout, System.Globalization.CultureInfo.InvariantCulture),
            PooledConnectionIdleTimeout = TimeSpan.Parse(Settings.PooledConnectionIdleTimeout,
                System.Globalization.CultureInfo.InvariantCulture),
            PooledConnectionLifetime = TimeSpan.Parse(Settings.PooledConnectionLifetime,
                System.Globalization.CultureInfo.InvariantCulture),
            EnableMultipleHttp2Connections = Settings.EnableMultipleHttp2Connections,
            MaxResponseHeadersLength = Settings.MaxResponseHeadersKilobytes,
            Expect100ContinueTimeout = TimeSpan.FromSeconds(1)
        };
    }
}

internal static class TransportProfiles
{
    public const string SyntheticProfileId = "baize-synthetic-recording-v1";
    public const string Http11ExactId = "baize-sockets-http11-exact-v1";
    public const string StateQualified = "Qualified";
    public const string StateUnreviewed = "Unreviewed";
    public const string StateSynthetic = "Synthetic";

    private static readonly string[] ReviewedClasses = ["ReviewedNetwork", "LoopbackDevelopment"];

    public static TransportProfileDefinition Http11Exact { get; } = new(
        Http11ExactId,
        "1",
        "SocketsHttpHandler",
        "1.1",
        "RequestVersionExact",
        new TransportHandlerSettings(
            AllowAutoRedirect: false,
            UseCookies: false,
            UseProxy: false,
            ProxyMode: "disabled",
            AutomaticDecompression: "None",
            AutomaticAuthentication: false,
            PreAuthenticate: false,
            CredentialsMode: "none",
            RetryMiddleware: false,
            MaxConnectionsPerServer: 1,
            ConnectTimeout: "00:00:30",
            PooledConnectionIdleTimeout: "00:00:30",
            PooledConnectionLifetime: "00:05:00",
            EnableMultipleHttp2Connections: false,
            MaxResponseHeadersKilobytes: 64,
            ExpectContinuePolicy: "reject",
            TlsMode: "system-default"),
        PhysicalAttemptUpperBound: 1,
        PhysicalAttemptBoundBasis:
            "http11-exact-post-with-content-no-expect-no-redirect-auth-cookie-proxy; " +
            "SocketsHttpHandler runtime v10.0.12 only retries connection failures when HttpConnection._canRetry is true, " +
            "which requires a contentless request or Expect: 100-continue after request headers were sent; " +
            "version fallback and HTTP/2 stream-limit retry paths are unreachable for RequestVersionExact HTTP/1.1",
        UnsupportedProtocolVersions: ["2.0", "3.0"],
        EvidenceRefs:
        [
            "docs/model-qualification-bq1b-stage4a-transport-audit.md#physical-attempt-bound",
            "docs/reviews/bq1b-stage4a-evidence/README.md"
        ]);

    private static readonly Lazy<RuntimeIdentity> Runtime = new(LoadRuntime, isThreadSafe: true);

    public static TransportProfileSnapshot Describe(string transportClass, string? profileRef)
    {
        TransportProfileSnapshot snapshot;
        if (transportClass == "SyntheticFake")
        {
            var selected = profileRef is null || profileRef == SyntheticProfileId;
            snapshot = new TransportProfileSnapshot(
                selected ? SyntheticProfileId : profileRef ?? SyntheticProfileId,
                selected ? "1" : "unreviewed",
                transportClass,
                selected ? StateSynthetic : StateUnreviewed,
                "RecordingTerminalHandler",
                "not-applicable",
                "not-applicable",
                null, null, null, null, null, null,
                null,
                "Synthetic execution never reaches a network terminal.",
                [], [], "");
        }
        else if (profileRef == Http11ExactId && ReviewedClasses.Contains(transportClass, StringComparer.Ordinal))
        {
            var runtime = Runtime.Value;
            var state = runtime.IsExact ? StateQualified : StateUnreviewed;
            snapshot = new TransportProfileSnapshot(
                Http11Exact.Id, Http11Exact.Revision, transportClass, state, Http11Exact.Handler,
                Http11Exact.ProtocolVersion, Http11Exact.ProtocolVersionPolicy, Http11Exact.Settings,
                runtime.Framework, runtime.RuntimeVersion, runtime.HttpAssemblyVersion,
                runtime.HttpAssemblyInformationalVersion, runtime.HttpAssemblyHash,
                state == StateQualified ? Http11Exact.PhysicalAttemptUpperBound : null,
                Http11Exact.PhysicalAttemptBoundBasis,
                Http11Exact.UnsupportedProtocolVersions, Http11Exact.EvidenceRefs, "");
        }
        else
        {
            snapshot = new TransportProfileSnapshot(
                profileRef ?? "missing", "unreviewed", transportClass, StateUnreviewed,
                "unspecified", "unspecified", "unspecified", null,
                null, null, null, null, null, null,
                "No reviewed physical-attempt bound exists for this selection.",
                [], [], "");
        }
        return snapshot with { Identity = ComputeIdentity(snapshot) };
    }

    public static string ComputeIdentity(TransportProfileSnapshot snapshot)
    {
        if (snapshot.ProfileId == SyntheticProfileId && snapshot.State == StateSynthetic)
        {
            return CanonicalJson.Identity(new
            {
                profileId = snapshot.ProfileId,
                transportClass = snapshot.TransportClass,
                handler = snapshot.Handler,
                state = snapshot.State
            });
        }
        if (snapshot.ProfileId == Http11ExactId)
        {
            return CanonicalJson.Identity(new
            {
                profileId = snapshot.ProfileId,
                revision = snapshot.Revision,
                transportClass = snapshot.TransportClass,
                state = snapshot.State,
                handler = snapshot.Handler,
                protocolVersion = snapshot.ProtocolVersion,
                protocolVersionPolicy = snapshot.ProtocolVersionPolicy,
                handlerSettings = snapshot.HandlerSettings,
                runtime = new
                {
                    framework = snapshot.RuntimeFramework,
                    runtimeVersion = snapshot.RuntimeVersion,
                    httpAssemblyVersion = snapshot.HttpAssemblyVersion,
                    httpAssemblyInformationalVersion = snapshot.HttpAssemblyInformationalVersion,
                    httpAssemblyHash = snapshot.HttpAssemblyHash
                },
                physicalAttemptUpperBound = snapshot.PhysicalAttemptUpperBound,
                physicalAttemptBoundBasis = snapshot.PhysicalAttemptBoundBasis,
                unsupportedProtocolVersions = snapshot.UnsupportedProtocolVersions,
                evidenceRefs = snapshot.EvidenceRefs
            });
        }
        return CanonicalJson.Identity(new
        {
            profileId = snapshot.ProfileId,
            transportClass = snapshot.TransportClass,
            state = snapshot.State,
            handler = snapshot.Handler
        });
    }

    public static bool IsQualified(TransportProfileSnapshot snapshot) =>
        snapshot.State == StateQualified
        && snapshot.ProfileId == Http11ExactId
        && snapshot.PhysicalAttemptUpperBound == Http11Exact.PhysicalAttemptUpperBound;

    public static bool IsCurrent(TransportProfileSnapshot snapshot)
    {
        if (snapshot.Identity != ComputeIdentity(snapshot)) return false;
        var current = Describe(snapshot.TransportClass, snapshot.ProfileId);
        return snapshot.State == current.State && snapshot.Identity == current.Identity;
    }

    private static RuntimeIdentity LoadRuntime()
    {
        var assembly = typeof(SocketsHttpHandler).Assembly;
        string? hash = null;
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                hash = CanonicalJson.Hash(File.ReadAllBytes(location));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException)
        {
        }
        return new RuntimeIdentity(
            RuntimeInformation.FrameworkDescription,
            Environment.Version.ToString(),
            assembly.GetName().Version?.ToString(),
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            hash);
    }

    private sealed record RuntimeIdentity(
        string Framework,
        string RuntimeVersion,
        string? HttpAssemblyVersion,
        string? HttpAssemblyInformationalVersion,
        string? HttpAssemblyHash)
    {
        public bool IsExact => !string.IsNullOrEmpty(HttpAssemblyVersion) && !string.IsNullOrEmpty(HttpAssemblyHash);
    }
}
