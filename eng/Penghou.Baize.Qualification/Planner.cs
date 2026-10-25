using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Baize.Qualification.Planning;

internal sealed class FrozenPlan(byte[] content)
{
    private readonly byte[] _content = content.ToArray();
    public byte[] Content => _content.ToArray();
    public string Hash => CanonicalJson.Hash(_content);
    public JsonElement Document => JsonDocument.Parse(_content).RootElement.Clone();
}

internal sealed class Planner(OfflineBoundary boundary)
{
    public JsonElement ValidateInventory(string json)
    {
        var inventory = CanonicalJson.Read<Inventory>(json);
        Validation.Inventory(inventory);
        var capabilities = OfflineResolution.Resolve(inventory, boundary);
        return CanonicalJson.Element(new
        {
            kind = "inventory-validation",
            schemaVersion = "1.0",
            commandCompleted = true,
            qualificationPerformed = false,
            executionAuthorized = false,
            models = capabilities.Keys.Order(StringComparer.Ordinal).ToArray(),
            inventoryHash = CanonicalJson.Identity(Normalize(CanonicalMoney(inventory)))
        });
    }

    public FrozenPlan Generate(string inventoryJson, string policyJson, string[] models, string[] contracts, JsonElement build)
    {
        var inventory = CanonicalJson.Read<Inventory>(inventoryJson);
        var policy = CanonicalJson.Read<ExecutionPolicy>(policyJson);
        Validation.Inventory(inventory);
        Validation.Policy(policy);
        Validation.Require(models.Length > 0 && contracts.Length > 0 && models.Distinct(StringComparer.Ordinal).Count() == models.Length
            && contracts.Distinct(StringComparer.Ordinal).Count() == contracts.Length, "empty-or-duplicate-selector");
        var selectedContracts = contracts.Select(ContractRegistry.Find).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        Validation.Require(selectedContracts.Any(c => c.Mode == "Invocation"), "streaming-requires-parent-scenario");
        var entries = models.Select(id => inventory.Entries.SingleOrDefault(e => e.Id == id)
            ?? throw new PlanningException("unknown-model")).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        Validation.Require(policy.EndpointAllowlist.All(id => inventory.Entries.Any(e => e.Endpoint.Id == id)), "unknown-endpoint-allowlist");
        var effective = OfflineResolution.Resolve(inventory, boundary);
        var normalizedInventory = Normalize(CanonicalMoney(inventory));
        var normalizedPolicy = Normalize(policy with
        {
            EndpointAllowlist = policy.EndpointAllowlist.Order(StringComparer.Ordinal).ToArray(),
            MaximumEstimatedCost = policy.MaximumEstimatedCost is null ? null : CanonicalJson.Money(policy.MaximumEstimatedCost)
        });
        var policyHash = CanonicalJson.Identity(normalizedPolicy);
        var subjects = new List<object>();
        var cases = new List<CasePlan>();
        foreach (var entry in entries)
        {
            var endpoint = entry.Endpoint;
            var provider = OfflineResolution.Provider(endpoint.Provider);
            var uri = Validation.EndpointUri(endpoint.BaseUrl ?? provider.DefaultBaseUrl, entry.TransportProfile);
            var caps = effective[entry.Id];
            var effectiveConfiguration = new
            {
                endpointId = endpoint.Id,
                provider = provider.Key.Value,
                providerModel = endpoint.ProviderModel ?? entry.Id,
                baseUrl = uri.AbsoluteUri.TrimEnd('/'),
                origin = uri.GetLeftPart(UriPartial.Authority),
                port = uri.Port,
                basePath = uri.AbsolutePath.TrimEnd('/'),
                endpoint.Profile,
                settings = Validation.Settings(endpoint, provider.Key.Value),
                clientRequestTimeout = endpoint.RequestTimeout ?? BaizeHttp.DefaultTimeout,
                effectiveAttemptCeiling = new[] { endpoint.RequestTimeout ?? BaizeHttp.DefaultTimeout, policy.AttemptTimeout }.Min(),
                capabilities = Normalize(caps),
                adapter = new
                {
                    assembly = provider.GetType().Assembly.GetName().Name,
                    version = provider.GetType().Assembly.GetName().Version!.ToString(),
                    buildRef = CanonicalJson.Identity(build)
                },
                identityEvidence = new
                {
                    canonicalCorrelation = "NotObserved",
                    nativeTools = "NotObserved",
                    providerIdentityProvenance = "NotObserved",
                    providerIdentityApplicability = entry.Protocol is "openai-chat-completions" or "anthropic-messages" ? "Applicable" : "Undetermined",
                    protocolRevisionEstablished = false
                }
            };
            var subject = new { requested = Normalize(CanonicalMoney(entry)), resolved = effectiveConfiguration, build };
            var subjectId = CanonicalJson.Identity(subject);
            subjects.Add(new { id = subjectId, modelId = entry.Id, binding = subject });
            foreach (var scenario in selectedContracts.Where(c => c.Mode == "Invocation"))
            {
                var exclusion = entry.Exclusions.SingleOrDefault(e => e.Contract == scenario.Id);
                var observationExclusion = entry.Exclusions.SingleOrDefault(e => e.Contract == QualificationContractsV3.Streaming.Id);
                if (exclusion is not null) ValidateExpiry(exclusion, policy);
                if (observationExclusion is not null) ValidateExpiry(observationExclusion, policy);
                var observed = selectedContracts.Where(c => c.Id == scenario.Id || c.Mode == "SharedObservation" && observationExclusion is null).ToArray();
                var blockers = new SortedSet<string>(StringComparer.Ordinal);
                if (entry.TransportProfile != "SyntheticFake")
                {
                    // Durable admission and the guarded transport exist; what
                    // remains is explicit operator authorization plus the
                    // unproven runtime-internal wire bounds (S0-G1/G6).
                    blockers.Add("live-execution-requires-explicit-operator-authorization");
                    blockers.Add("wire-attempt-bound-unproven");
                    blockers.Add("wire-and-preallocation-byte-bounds-unproven");
                }
                else if (!SyntheticTransports.Implemented(provider.Key.Value))
                {
                    blockers.Add("synthetic-transport-not-implemented");
                }
                if (entry.ApiVersion is null) blockers.Add("protocol-revision-unknown");
                else blockers.Add("protocol-revision-evidence-unverified");
                if (entry.Revision is null) blockers.Add("model-revision-unknown");
                if (!policy.EndpointAllowlist.Contains(endpoint.Id, StringComparer.Ordinal)) blockers.Add("endpoint-not-allowed");
                if (scenario.RequiresNativeTools && !caps.NativeToolCalling) blockers.Add("native-tool-configuration-disabled");
                if (entry.EconomicsRef is null) blockers.Add("economics-unknown");
                if (entry.AllowanceRef is null || !entry.AllowanceVerified) blockers.Add("allowance-unverified");
                var bounds = entry.Bounds.SingleOrDefault(b => b.Contract == scenario.Id);
                if (bounds?.InputTokens is null || bounds.OutputTokens is null || bounds.ReasoningTokens is null || bounds.TotalTokens is null)
                    blockers.Add("token-reservation-bound-unknown");
                else blockers.Add("token-bound-enforcement-unverified");
                if (bounds?.MaximumCost is null) blockers.Add("cost-reservation-bound-unknown");
                else blockers.Add("economic-bound-evidence-unverified");
                var caseBinding = new { subjectId, contracts = observed, policyHash };
                var caseId = CanonicalJson.Identity(caseBinding);
                cases.Add(new CasePlan(caseId, subjectId, entry.Id, scenario.Id, observed, exclusion is null ? "Applicable" : "Excluded", exclusion,
                    observationExclusion, Enumerable.Range(1, policy.RepeatCount).Select(slot => new TrialSlot(slot,
                        CanonicalJson.Identity(new { caseId, slot }))).ToArray(),
                    exclusion is null ? scenario.MaximumDispatches : 0, exclusion is null ? scenario.MaximumDispatches : 0,
                    entry.TransportProfile == "SyntheticFake" ? (exclusion is null ? scenario.MaximumDispatches : 0) : null,
                    bounds, "Blocked", blockers.ToArray()));
            }
        }
        Validation.Require(cases.Any(c => c.Applicability == "Applicable"), "all-cases-excluded");
        var active = cases.Where(c => c.Applicability == "Applicable").ToArray();
        var count = checked(active.Sum(c => (long)c.MaximumSemanticInvocationsPerSlot) * policy.RepeatCount);
        var input = Sum(active, c => c.RequestedReservation?.InputTokens, policy.RepeatCount);
        var output = Sum(active, c => c.RequestedReservation?.OutputTokens, policy.RepeatCount);
        var reasoning = Sum(active, c => c.RequestedReservation?.ReasoningTokens, policy.RepeatCount);
        var total = Sum(active, c => c.RequestedReservation?.TotalTokens, policy.RepeatCount);
        var currencies = active.Select(c => c.RequestedReservation?.Currency).Distinct(StringComparer.Ordinal).ToArray();
        var costKnown = active.All(c => c.RequestedReservation?.MaximumCost is not null) && currencies.Length == 1;
        decimal? cost = costKnown ? checked(active.Sum(c => decimal.Parse(c.RequestedReservation!.MaximumCost!, CultureInfo.InvariantCulture)) * policy.RepeatCount) : null;
        var runBlockers = new SortedSet<string>(active.SelectMany(c => c.LiveEligibilityBlockers), StringComparer.Ordinal);
        if (count > policy.MaximumModelCalls || count > policy.MaximumHttpRequests) runBlockers.Add("request-envelope-insufficient");
        CheckLimit(input, policy.MaximumInputTokens, "input", runBlockers);
        CheckLimit(output, policy.MaximumOutputTokens, "output", runBlockers);
        CheckLimit(reasoning, policy.MaximumReasoningTokens, "reasoning", runBlockers);
        CheckLimit(total, policy.MaximumTotalTokens, "total", runBlockers);
        if (cost is null || policy.MaximumEstimatedCost is null) runBlockers.Add("cost-envelope-unknown");
        else if (currencies[0] != policy.Currency || cost > decimal.Parse(policy.MaximumEstimatedCost, CultureInfo.InvariantCulture)) runBlockers.Add("cost-envelope-insufficient-or-currency-mismatch");
        var document = new
        {
            kind = "qualification-plan",
            schemaVersion = "1.0",
            canonicalWriter = CanonicalJson.Version,
            executionAuthorized = false,
            qualificationPerformed = false,
            liveEligibility = "Blocked",
            liveEligibilityBlockers = runBlockers.ToArray(),
            source = new { build, inventoryHash = CanonicalJson.Identity(normalizedInventory), policyHash, contractRegistryHash = CanonicalJson.Identity(ContractRegistry.Current) },
            inventory = normalizedInventory,
            policy = normalizedPolicy,
            selectedModels = entries.Select(e => e.Id).ToArray(),
            selectedContracts,
            subjects,
            cases,
            requirements = new
            {
                semanticInvocations = count,
                neutralHttpCalls = count,
                terminalBackendSends = count,
                physicalWireAttempts = active.All(c => c.MaximumPhysicalWireAttemptsPerSlot is not null) ? (long?)count : null,
                quantityStatus = "PlannedRequirementsNotObservedOrEnforced",
                inputTokens = input,
                outputTokens = output,
                reasoningTokens = reasoning,
                totalTokens = total,
                maximumCost = cost is null ? null : CanonicalJson.Decimal(cost.Value),
                currency = costKnown ? currencies[0] : null,
                reservationStatus = "RequestedBoundsRequireEvidenceAndEnforcement"
            },
            requiredEvidence = new[] { "build-and-contract-binding", "configuration-and-protocol-binding", "durable-reservation-before-credentials",
                "durable-boundary-specific-dispatch-intent", "usage-completeness-and-bound-sources", "cooperative-cleanup-status" },
            limitations = new[] { "Planning grants no execution authority or capability claim.", "Declared bounds and references are not verified enforcement.",
                "Unknown revision stays unknown; aliases do not identify weights.", "OpenAI F1 adapter ordering defect remains separately attributed; it cannot establish unsupportedness.",
                "Native validity, canonical correlation and provider provenance remain separate observations." }
        };
        return new FrozenPlan(CanonicalJson.Write(document));
    }

    private static void ValidateExpiry(Exclusion exclusion, ExecutionPolicy policy) => Validation.Require(
        exclusion.ReviewExpires >= DateOnly.FromDateTime(policy.AsOfUtc.UtcDateTime), "expired-exclusion");
    // Money is a decimal string; normalize equivalent spellings ("0.10"/"0.1")
    // so equal economics cannot produce different identities.
    private static ModelEntry CanonicalMoney(ModelEntry entry) => entry with
    {
        Bounds = entry.Bounds.Select(bound => bound.MaximumCost is null
            ? bound
            : bound with { MaximumCost = CanonicalJson.Money(bound.MaximumCost) }).ToArray()
    };
    private static Inventory CanonicalMoney(Inventory inventory) => inventory with
    {
        Entries = inventory.Entries.Select(CanonicalMoney).ToArray()
    };
    private static long? Sum(CasePlan[] cases, Func<CasePlan, long?> read, int repetitions)
        => cases.Any(c => read(c) is null) ? null : checked(cases.Sum(c => read(c)!.Value) * repetitions);
    private static void CheckLimit(long? required, long? allowed, string unit, ISet<string> blockers)
    {
        if (required is null || allowed is null) blockers.Add(unit + "-envelope-unknown");
        else if (required > allowed) blockers.Add(unit + "-envelope-insufficient");
    }

    internal static JsonElement Normalize(object value)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(value, CanonicalJson.Options))!;
        void Sort(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var property in obj.ToArray())
                {
                    Sort(property.Value);
                    if (property.Value is not JsonArray array) continue;
                    string? key = property.Key switch { "entries" => "id", "exclusions" or "bounds" => "contract", "advertised" => "facet", _ => null };
                    if (key is not null || property.Key is "contentTypes" or "supportedThinkingEfforts" or "endpointAllowlist")
                    {
                        var sorted = array.OrderBy(item => key is null ? item!.ToJsonString() : item![key]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.DeepClone()).ToArray();
                        obj[property.Key] = new JsonArray(sorted);
                    }
                }
            }
            else if (current is JsonArray array) foreach (var item in array) Sort(item);
        }
        Sort(node);
        return JsonSerializer.SerializeToElement(node, CanonicalJson.Options);
    }
}

internal sealed record TrialSlot(int Slot, string Id);
internal sealed record CasePlan(string Id, string SubjectId, string ModelId, string Scenario, PlannedContract[] Observations,
    string Applicability, Exclusion? Exclusion, Exclusion? StreamingExclusion, TrialSlot[] Trials,
    int MaximumSemanticInvocationsPerSlot, int MaximumNeutralHttpCallsPerSlot, int? MaximumPhysicalWireAttemptsPerSlot,
    TokenEnvelope? RequestedReservation, string LiveEligibility, string[] LiveEligibilityBlockers);

internal static class SyntheticTransports
{
    // Only the OpenAI chat-completions synthetic terminal transport exists in
    // this stage; other synthetic protocols stay explicitly blocked rather
    // than silently exercising a parallel execution path.
    public static bool Implemented(string providerKey) => providerKey is "OpenAi";
}
