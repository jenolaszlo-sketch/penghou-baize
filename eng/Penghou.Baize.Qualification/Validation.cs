using System.Globalization;
using System.Text.RegularExpressions;
using Penghou.Baize.Router.Configuration;

namespace Penghou.Baize.Qualification.Planning;

internal static partial class Validation
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
    [GeneratedRegex("^/[A-Za-z0-9/_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
    public static void Require(bool condition, string code)
    {
        if (!condition) throw new PlanningException(code);
    }
    public static void Reference(string? value) => Require(value is not null && ReferencePattern().IsMatch(value), "invalid-reference");
    private static void OptionalReference(string? value) { if (value is not null) Reference(value); }
    public static void Header(string? kind, string? version, string expected)
        => Require(kind == expected && version == "1.0", "unsupported-schema");

    public static void Inventory(Inventory inventory)
    {
        Header(inventory.Kind, inventory.SchemaVersion, "qualification-inventory");
        Require(inventory.Entries is { Length: > 0 and <= 256 } && inventory.Profiles is not null, "invalid-inventory-size");
        foreach (var profile in inventory.Profiles!) { Reference(profile.Key); Capabilities(profile.Value); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in inventory.Entries!)
        {
            Require(entry is not null, "null-entry");
            Reference(entry!.Id); Reference(entry.Service); Reference(entry.EndpointProfile); Reference(entry.AccountScopeRef);
            OptionalReference(entry.ApiVersion); OptionalReference(entry.Revision);
            OptionalReference(entry.EconomicsRef); OptionalReference(entry.AllowanceRef);
            OptionalReference(entry.TransportProfileRef);
            Require(ids.Add(entry.Id), "duplicate-model");
            Require(entry.ConfigurationVisibility == "PublicConfiguration", "private-configuration-not-supported");
            Require(entry.TransportProfile is "SyntheticFake" or "ReviewedNetwork" or "LoopbackDevelopment", "unknown-transport-profile");
            Require(entry.TransportProfile != "SyntheticFake" || entry.TransportProfileRef is null
                or TransportProfiles.SyntheticProfileId, "invalid-synthetic-transport-profile-ref");
            Require(entry.Endpoint is not null && entry.Advertised is not null && entry.Exclusions is not null && entry.Bounds is not null, "null-configuration");
            var endpoint = entry.Endpoint!;
            Reference(endpoint.Id);
            Require(endpoints.Add(endpoint.Id!), "duplicate-endpoint");
            var provider = OfflineResolution.Provider(endpoint.Provider);
            Require(ModelPattern().IsMatch(endpoint.ProviderModel ?? entry.Id), "invalid-model-name");
            OptionalReference(endpoint.ApiKeySecretName); OptionalReference(endpoint.Profile);
            Require(endpoint.Profile is null || inventory.Profiles.ContainsKey(endpoint.Profile), "unknown-profile");
            Require(endpoint.RequestTimeout is null || endpoint.RequestTimeout > TimeSpan.Zero && endpoint.RequestTimeout <= TimeSpan.FromDays(1), "invalid-endpoint-timeout");
            var protocol = provider.Key.Value switch
            {
                "OpenAi" => "openai-chat-completions",
                "Claude" => "anthropic-messages",
                "Ollama" => "ollama-chat",
                "Gemini" => "gemini-generate-content",
                _ => ""
            };
            Require(entry.Protocol == protocol, "unsupported-provider-protocol");
            var uri = EndpointUri(endpoint.BaseUrl ?? provider.DefaultBaseUrl, entry.TransportProfile);
            // One profile is one destination/account/service binding, not an alias
            // for two hosts. Distinct model IDs within it remain legal.
            var binding = CanonicalJson.Identity(new { entry.Service, entry.AccountScopeRef, uri = uri.AbsoluteUri, entry.Protocol, entry.ApiVersion, entry.TransportProfile, transportProfileRef = entry.TransportProfileRef ?? "missing" });
            Require(!profiles.TryGetValue(entry.EndpointProfile, out var prior) || prior == binding, "conflicting-endpoint-profile");
            profiles[entry.EndpointProfile] = binding;
            Settings(endpoint, provider.Key.Value);
            if (endpoint.Capabilities is not null) Capabilities(endpoint.Capabilities);
            var advertised = new HashSet<string>(StringComparer.Ordinal);
            foreach (var claim in entry.Advertised!)
            {
                Require(claim is not null, "null-advertised");
                Reference(claim!.Facet); Reference(claim.SourceRef);
                Require(typeof(LlmEndpointCapabilities).GetProperty(claim.Facet) is not null && claim.ObservedOn != default && advertised.Add(claim.Facet), "invalid-advertised");
            }
            var exclusions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exclusion in entry.Exclusions!)
            {
                Require(exclusion is not null, "null-exclusion");
                ContractRegistry.Find(exclusion!.Contract);
                Reference(exclusion.ReasonCode); Reference(exclusion.OwnerRef); Reference(exclusion.SourceRef);
                Require(exclusions.Add(exclusion.Contract) && exclusion.ReviewExpires != default, "invalid-exclusion");
            }
            var bounds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bound in entry.Bounds!)
            {
                Require(bound is not null, "null-bound");
                var contract = ContractRegistry.Find(bound!.Contract);
                Require(contract.Mode == "Invocation" && bounds.Add(bound.Contract), "invalid-contract-bound");
                Reference(bound.SourceRef);
                PositiveOrUnknown(bound.InputTokens); PositiveOrUnknown(bound.OutputTokens);
                // Explicit zero reasoning means a reviewed no-additional-reasoning bound.
                Require(bound.ReasoningTokens is null or >= 0, "invalid-reasoning-bound");
                PositiveOrUnknown(bound.TotalTokens);
                Require(bound.OutputTokens is null || bound.OutputTokens >= contract.MaximumOutputTokens, "bound-below-fixture-cap");
                Require(bound.TotalTokens is null || bound.TotalTokens >= (bound.InputTokens ?? 0) && bound.TotalTokens >= (bound.OutputTokens ?? 0)
                    && bound.TotalTokens >= (bound.ReasoningTokens ?? 0), "contradictory-token-bound");
                Money(bound.MaximumCost, bound.Currency);
            }
            Require(!entry.AllowanceVerified || entry.AllowanceRef is not null, "verified-allowance-without-reference");
        }
    }

    public static Uri EndpointUri(string value, string transport)
    {
        Require(Uri.TryCreate(value, UriKind.Absolute, out var uri), "invalid-endpoint");
        Require(uri!.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && !value.Contains('%') && !value.Contains('\\') && !value.Contains("/./", StringComparison.Ordinal)
            && !value.Contains("..", StringComparison.Ordinal) && uri.AbsolutePath.Length <= 128 && PathPattern().IsMatch(uri.AbsolutePath), "unsafe-endpoint");
        Require(uri.Scheme == "https" || transport == "LoopbackDevelopment" && uri.Scheme == "http" && uri.IsLoopback, "endpoint-requires-https");
        Require(transport != "SyntheticFake" || uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase), "synthetic-endpoint-must-be-invalid");
        return uri;
    }

    public static SortedDictionary<string, string> Settings(LlmEndpointOptions endpoint, string provider)
    {
        Require(endpoint.Settings is not null, "null-settings");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var settings = new Dictionary<string, string>(endpoint.Settings!, StringComparer.OrdinalIgnoreCase);
        if (endpoint.Dialect is not null) settings.TryAdd("Dialect", endpoint.Dialect);
        if (endpoint.ThinkingStyle is not null) settings.TryAdd("ThinkingStyle", endpoint.ThinkingStyle);
        foreach (var item in settings)
        {
            Require(item.Value is not null, "invalid-setting");
            if (provider == "OpenAi" && item.Key.Equals("Dialect", StringComparison.OrdinalIgnoreCase))
            {
                Require(Enum.TryParse<OpenAi.OpenAiDialect>(item.Value, true, out var dialect) && Enum.IsDefined(dialect) && !int.TryParse(item.Value, out _), "unknown-dialect");
                result.Add("Dialect", dialect.ToString());
            }
            else if (provider == "Claude" && item.Key.Equals("ThinkingStyle", StringComparison.OrdinalIgnoreCase))
            {
                Require(Enum.TryParse<Claude.ClaudeThinkingStyle>(item.Value, true, out var style) && Enum.IsDefined(style) && !int.TryParse(item.Value, out _), "unknown-thinking-style");
                result.Add("ThinkingStyle", style.ToString());
            }
            else throw new PlanningException("unsupported-setting");
        }
        if (provider == "OpenAi") result.TryAdd("Dialect", OpenAi.OpenAiDialect.Standard.ToString());
        if (provider == "Claude") result.TryAdd("ThinkingStyle", Claude.ClaudeThinkingStyle.Adaptive.ToString());
        return result;
    }

    private static void Capabilities(LlmEndpointCapabilitiesOptions? options)
    {
        Require(options is not null, "null-capability-profile");
        Require(options!.ThinkingBudget is null or > 0, "invalid-thinking-budget");
        if (options.ContentTypes is not null)
            Require(options.ContentTypes.All(Enum.IsDefined) && options.ContentTypes.Distinct().Count() == options.ContentTypes.Count, "invalid-content-types");
        if (options.SupportedThinkingEfforts is not null)
            Require(options.SupportedThinkingEfforts.All(Enum.IsDefined) && options.SupportedThinkingEfforts.Distinct().Count() == options.SupportedThinkingEfforts.Count, "invalid-thinking-efforts");
        if (options.Batch is not null) Flags(options.Batch.Value);
        if (options.ContentTransports is not null)
            foreach (var item in options.ContentTransports) { Require(Enum.IsDefined(item.Key), "invalid-content-type"); Flags(item.Value); }
    }
    private static void Flags<T>(T value) where T : struct, Enum
    {
        var mask = Enum.GetValues<T>().Aggregate(0L, (acc, item) => acc | Convert.ToInt64(item, CultureInfo.InvariantCulture));
        Require((Convert.ToInt64(value, CultureInfo.InvariantCulture) & ~mask) == 0, "invalid-flags");
    }
    private static void PositiveOrUnknown(long? value) => Require(value is null or > 0, "nonpositive-limit");
    private static void Money(string? amount, string? currency)
    {
        Require((amount is null) == (currency is null), "incomplete-money");
        if (amount is null) return;
        CanonicalJson.Money(amount);
        Require(currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z'), "invalid-currency");
    }

    public static void Policy(ExecutionPolicy policy)
    {
        Header(policy.Kind, policy.SchemaVersion, "qualification-policy");
        Require(policy.AsOfUtc != default && policy.AsOfUtc.Offset == TimeSpan.Zero, "invalid-as-of");
        Require(policy.MaximumHttpRequests > 0 && policy.MaximumModelCalls > 0 && policy.MaximumConcurrentModels > 0
            && policy.MaximumConcurrentRequestsPerAccount > 0 && policy.RepeatCount is > 0 and <= 1000, "nonpositive-limit");
        Require(policy.MinimumRequestSpacing > TimeSpan.Zero && policy.MaximumDuration > TimeSpan.Zero && policy.MaximumDuration <= TimeSpan.FromDays(1)
            && policy.AttemptTimeout > TimeSpan.Zero && policy.AttemptTimeout <= policy.MaximumDuration
            && policy.StreamIdleTimeout > TimeSpan.Zero && policy.StreamIdleTimeout <= policy.AttemptTimeout
            && policy.CleanupGrace == TimeSpan.FromSeconds(5), "invalid-deadlines");
        Require(policy.MaximumRequestBytes > 0 && policy.MaximumResponseBytes > 0 && policy.MaximumEventBytes > 0
            && policy.MaximumEvidenceBytes > 0 && policy.MaximumEventBytes <= policy.MaximumResponseBytes, "invalid-byte-limits");
        Require(policy.MaximumAvailabilityRetries == 0 && policy.CapturePolicy == "None", "unsupported-policy-feature");
        PositiveOrUnknown(policy.MaximumInputTokens); PositiveOrUnknown(policy.MaximumOutputTokens); PositiveOrUnknown(policy.MaximumTotalTokens);
        Require(policy.MaximumReasoningTokens is null or >= 0, "invalid-reasoning-bound");
        Require(policy.MaximumTotalTokens is null || policy.MaximumTotalTokens >= (policy.MaximumInputTokens ?? 0)
            && policy.MaximumTotalTokens >= (policy.MaximumOutputTokens ?? 0) && policy.MaximumTotalTokens >= (policy.MaximumReasoningTokens ?? 0), "contradictory-token-limit");
        Money(policy.MaximumEstimatedCost, policy.Currency); OptionalReference(policy.EconomicsSourceRef);
        Require(policy.MaximumEstimatedCost is null || policy.EconomicsSourceRef is not null, "missing-economics-source");
        Require(policy.EndpointAllowlist is { Length: > 0 } && policy.EndpointAllowlist.Distinct(StringComparer.Ordinal).Count() == policy.EndpointAllowlist.Length, "invalid-endpoint-allowlist");
        foreach (var endpoint in policy.EndpointAllowlist!) Reference(endpoint);
    }
}
