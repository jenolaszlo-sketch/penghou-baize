using Penghou.Baize.Router.Configuration;

namespace Penghou.Baize.Qualification.Planning;

// Authoritative JSON is copied at entry and never exposed as mutable resolved state.
internal sealed record Inventory
{
    public required string Kind { get; init; }
    public required string SchemaVersion { get; init; }
    public Dictionary<string, LlmEndpointCapabilitiesOptions> Profiles { get; init; } = [];
    public required ModelEntry[] Entries { get; init; }
}

internal sealed record ModelEntry
{
    public required string Id { get; init; }
    public required string Service { get; init; }
    public required string EndpointProfile { get; init; }
    public required string AccountScopeRef { get; init; }
    public required string Protocol { get; init; }
    public required string? ApiVersion { get; init; }
    public required string? Revision { get; init; }
    public required bool MutableAlias { get; init; }
    public required string ConfigurationVisibility { get; init; }
    public required string TransportProfile { get; init; }
    public string? TransportProfileRef { get; init; }
    public required LlmEndpointOptions Endpoint { get; init; }
    public AdvertisedCapability[] Advertised { get; init; } = [];
    public Exclusion[] Exclusions { get; init; } = [];
    public required string? EconomicsRef { get; init; }
    public required string? AllowanceRef { get; init; }
    public required bool AllowanceVerified { get; init; }
    public TokenEnvelope[] Bounds { get; init; } = [];
}

internal sealed record AdvertisedCapability(string Facet, bool? Value, string SourceRef, DateOnly ObservedOn);
internal sealed record Exclusion(string Contract, string ReasonCode, string SourceRef, string OwnerRef, DateOnly ReviewExpires);
internal sealed record TokenEnvelope(string Contract, long? InputTokens, long? OutputTokens, long? ReasoningTokens,
    long? TotalTokens, string? MaximumCost, string? Currency, string SourceRef);

internal sealed record ExecutionPolicy
{
    public required string Kind { get; init; }
    public required string SchemaVersion { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required long MaximumHttpRequests { get; init; }
    public required long MaximumModelCalls { get; init; }
    public int MaximumConcurrentModels { get; init; } = 1;
    public int MaximumConcurrentRequestsPerAccount { get; init; } = 1;
    public TimeSpan MinimumRequestSpacing { get; init; } = TimeSpan.FromSeconds(1);
    public required long? MaximumInputTokens { get; init; }
    public required long? MaximumOutputTokens { get; init; }
    public required long? MaximumReasoningTokens { get; init; }
    public required long? MaximumTotalTokens { get; init; }
    public required string? MaximumEstimatedCost { get; init; }
    public required string? Currency { get; init; }
    public required string? EconomicsSourceRef { get; init; }
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupGrace { get; init; } = TimeSpan.FromSeconds(5);
    public long MaximumRequestBytes { get; init; } = 1 << 20;
    public long MaximumResponseBytes { get; init; } = 4 << 20;
    public long MaximumEventBytes { get; init; } = 64 << 10;
    public long MaximumEvidenceBytes { get; init; } = 16 << 20;
    public int RepeatCount { get; init; } = 3;
    public int MaximumAvailabilityRetries { get; init; }
    public required string[] EndpointAllowlist { get; init; }
    public string CapturePolicy { get; init; } = "None";
}

internal sealed class PlanningException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
