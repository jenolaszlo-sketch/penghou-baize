using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

// The operator authorization for one exact live cohort. It is intentionally a
// separate, frozen artifact: the explicit run-live command is not enough, and
// neither is --allow-live. Every material field is re-checked against the
// frozen plan before a run directory, credential read or dispatch exists.
internal sealed record LiveAuthorizationCase(
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string[] ObservedContractIds,
    int MaximumSemanticInvocations,
    int MaximumPhysicalAttempts);

internal sealed record LiveAuthorizationDocument(
    string Kind,
    string SchemaVersion,
    string AuthorizationId,
    string ConsumptionAuthorityId,
    string ProposalDigest,
    string Signature,
    string AuthorizationRef,
    string OperatorRef,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    string PlanHash,
    string ContractRegistryHash,
    string InventoryHash,
    string PolicyHash,
    string TransportProfileId,
    string TransportProfileIdentity,
    string Provider,
    string EndpointId,
    string EndpointBaseUrl,
    string EndpointOrigin,
    string ProviderModel,
    string Protocol,
    string? ApiVersion,
    string CredentialRef,
    string ArtifactDestination,
    IReadOnlyDictionary<string, string> Settings,
    LiveAuthorizationCase[] Cases,
    BudgetEnvelopeEvidence Budget);

internal sealed record ValidatedLiveAuthorization(
    LiveAuthorizationDocument Document,
    string AuthorizationHash,
    RunCaseSelection[] Selection,
    BudgetEnvelopeEvidence Budget);

internal static class LiveAuthorizations
{
    public const string Kind = "qualification-live-authorization";
    public const string SchemaVersion = "2.0";
    public static readonly TimeSpan MaximumValidity = TimeSpan.FromMinutes(10);

    public static LiveAuthorizationDocument Read(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ExecutionException("live-authorization-unreadable");
        }
        if (bytes.Length > 1 << 20) throw new ExecutionException("live-authorization-too-large");
        try
        {
            var document = CanonicalJson.Read<LiveAuthorizationDocument>(System.Text.Encoding.UTF8.GetString(bytes));
            if (document.Kind != Kind || document.SchemaVersion != SchemaVersion)
                throw new ExecutionException("live-authorization-invalid");
            if (!Guid.TryParseExact(document.AuthorizationId, "N", out var authorizationId)
                || authorizationId.ToString("N") != document.AuthorizationId
                || !Guid.TryParseExact(document.ConsumptionAuthorityId, "N", out var authorityId)
                || authorityId.ToString("N") != document.ConsumptionAuthorityId)
                throw new ExecutionException("live-authorization-invalid");
            if (!Sha256Reference(document.ProposalDigest))
                throw new ExecutionException("live-authorization-invalid");
            if (!Reference(document.AuthorizationRef) || !Reference(document.OperatorRef)
                || !Reference(document.CredentialRef) || string.IsNullOrWhiteSpace(document.ArtifactDestination))
                throw new ExecutionException("live-authorization-invalid");
            if (document.Cases is not { Length: > 0 } || document.Settings is null || document.Budget is null)
                throw new ExecutionException("live-authorization-invalid");
            return document;
        }
        catch (ExecutionException) { throw; }
        catch (Exception exception) when (exception is JsonException or PlanningException or FormatException
            or ArgumentException or OverflowException)
        {
            throw new ExecutionException("live-authorization-invalid");
        }
    }

    public static ValidatedLiveAuthorization Validate(FrozenRunPlan plan, LiveAuthorizationDocument authorization,
        string runDirectory, DateTimeOffset now, RunCaseSelection[]? expectedSelection = null)
    {
        LiveAuthorizationAuthentication.Verify(authorization);
        ValidateValidity(authorization, now);
        var source = plan.Document.GetProperty("source");
        if (authorization.PlanHash != plan.PlanHash
            || authorization.ContractRegistryHash != plan.ContractRegistryHash
            || authorization.InventoryHash != source.GetProperty("inventoryHash").GetString()
            || authorization.PolicyHash != source.GetProperty("policyHash").GetString())
            throw new ExecutionException("live-authorization-plan-mismatch");
        if (!PathEquals(authorization.ArtifactDestination, runDirectory))
            throw new ExecutionException("live-authorization-output-mismatch");

        var selection = Select(plan, authorization);
        if (expectedSelection is not null && !SameSelection(selection, expectedSelection))
            throw new ExecutionException("live-authorization-selection-mismatch");

        ValidateSubjects(plan, authorization, selection);
        ValidateBudget(plan, authorization, selection);
        var hash = CanonicalJson.Identity(authorization);
        return new ValidatedLiveAuthorization(authorization, hash, selection, authorization.Budget);
    }

    public static void ValidateValidity(LiveAuthorizationDocument authorization, DateTimeOffset now)
    {
        if (authorization.IssuedUtc.Offset != TimeSpan.Zero || authorization.ExpiresUtc.Offset != TimeSpan.Zero
            || authorization.ExpiresUtc <= authorization.IssuedUtc)
            throw new ExecutionException("live-authorization-time-invalid");
        if (authorization.ExpiresUtc - authorization.IssuedUtc > MaximumValidity)
            throw new ExecutionException("live-authorization-validity-too-long");
        if (authorization.IssuedUtc > now.ToUniversalTime())
            throw new ExecutionException("live-authorization-not-yet-valid");
        if (now.ToUniversalTime() >= authorization.ExpiresUtc)
            throw new ExecutionException("live-authorization-expired");
    }

    private static RunCaseSelection[] Select(FrozenRunPlan plan, LiveAuthorizationDocument authorization)
    {
        var selected = new List<RunCaseSelection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var authCase in authorization.Cases)
        {
            if (!seen.Add(authCase.CaseId + "|" + authCase.TrialId))
                throw new ExecutionException("live-authorization-selection-mismatch");
            var planCase = plan.Cases.SingleOrDefault(c => c.GetProperty("id").GetString() == authCase.CaseId);
            if (planCase.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException("live-authorization-selection-mismatch");
            if (planCase.GetProperty("applicability").GetString() != "Applicable")
                throw new ExecutionException("live-authorization-selection-mismatch");
            var semanticElement = planCase.GetProperty("maximumSemanticInvocationsPerSlot");
            var physicalElement = planCase.GetProperty("maximumPhysicalWireAttemptsPerSlot");
            if (planCase.GetProperty("modelId").GetString() != authCase.ModelId
                || planCase.GetProperty("scenario").GetString() != authCase.ScenarioId
                || semanticElement.ValueKind != JsonValueKind.Number
                || physicalElement.ValueKind != JsonValueKind.Number
                || semanticElement.GetInt32() != authCase.MaximumSemanticInvocations
                || physicalElement.GetInt32() != authCase.MaximumPhysicalAttempts)
                throw new ExecutionException("live-authorization-selection-mismatch");
            var observed = planCase.GetProperty("observations").EnumerateArray()
                .Select(o => o.GetProperty("id").GetString()!).ToArray();
            if (!observed.SequenceEqual(authCase.ObservedContractIds, StringComparer.Ordinal))
                throw new ExecutionException("live-authorization-selection-mismatch");
            var trials = planCase.GetProperty("trials").EnumerateArray().ToArray();
            var trial = trials.SingleOrDefault(t => t.GetProperty("id").GetString() == authCase.TrialId
                && t.GetProperty("slot").GetInt32() == authCase.TrialSlot);
            if (trial.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException("live-authorization-selection-mismatch");
            var subjectId = planCase.GetProperty("subjectId").GetString()!;
            var subject = plan.Subjects.SingleOrDefault(s => s.GetProperty("id").GetString() == subjectId);
            if (subject.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException("live-authorization-selection-mismatch");
            selected.Add(new RunCaseSelection(authCase.CaseId, subjectId, authCase.ModelId, authCase.ScenarioId,
                planCase, subject, trial.GetProperty("id").GetString()!, trial.GetProperty("slot").GetInt32()));
        }
        if (selected.Count == 0) throw new ExecutionException("live-authorization-selection-mismatch");
        return selected.ToArray();
    }

    private static void ValidateSubjects(FrozenRunPlan plan, LiveAuthorizationDocument authorization,
        RunCaseSelection[] selection)
    {
        string? endpointId = null;
        string? provider = null;
        string? providerModel = null;
        string? protocol = null;
        string? apiVersion = null;
        foreach (var selected in selection)
        {
            var binding = selected.SubjectElement.GetProperty("binding");
            var requested = binding.GetProperty("requested");
            var resolved = binding.GetProperty("resolved");
            var transportClass = requested.GetProperty("transportProfile").GetString();
            if (transportClass == "SyntheticFake")
                throw new ExecutionException("live-command-requires-real-profile");
            if (requested.GetProperty("transportProfileRef").ValueKind != JsonValueKind.String
                || requested.GetProperty("transportProfileRef").GetString() != authorization.TransportProfileId
                || resolved.GetProperty("transport").GetProperty("profileId").GetString() != authorization.TransportProfileId
                || resolved.GetProperty("transport").GetProperty("identity").GetString() != authorization.TransportProfileIdentity)
                throw new ExecutionException("live-authorization-profile-mismatch");
            var recorded = resolved.GetProperty("transport").Deserialize<TransportProfileSnapshot>(CanonicalJson.Options)
                ?? throw new ExecutionException("live-authorization-profile-mismatch");
            if (!TransportProfiles.IsCurrent(recorded) || !TransportProfiles.IsQualified(recorded))
                throw new ExecutionException("live-authorization-profile-mismatch");

            var requestedEndpoint = requested.GetProperty("endpoint");
            var resolvedSettings = resolved.GetProperty("settings").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
            if (resolved.GetProperty("endpointId").GetString() != authorization.EndpointId
                || resolved.GetProperty("provider").GetString() != authorization.Provider
                || resolved.GetProperty("providerModel").GetString() != authorization.ProviderModel
                || resolved.GetProperty("baseUrl").GetString() != authorization.EndpointBaseUrl
                || resolved.GetProperty("origin").GetString() != authorization.EndpointOrigin
                || requested.GetProperty("protocol").GetString() != authorization.Protocol
                || NullableString(requested, "apiVersion") != authorization.ApiVersion
                || requestedEndpoint.GetProperty("apiKeySecretName").GetString() != authorization.CredentialRef
                || !resolvedSettings.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .SequenceEqual(authorization.Settings.OrderBy(p => p.Key, StringComparer.Ordinal)))
                throw new ExecutionException("live-authorization-endpoint-mismatch");
            if (endpointId is null)
            {
                endpointId = authorization.EndpointId;
                provider = authorization.Provider;
                providerModel = authorization.ProviderModel;
                protocol = authorization.Protocol;
                apiVersion = authorization.ApiVersion;
            }
            else if (endpointId != authorization.EndpointId || provider != authorization.Provider
                || providerModel != authorization.ProviderModel || protocol != authorization.Protocol
                || apiVersion != authorization.ApiVersion)
            {
                throw new ExecutionException("live-authorization-mixed-subjects");
            }
        }
    }

    private static void ValidateBudget(FrozenRunPlan plan, LiveAuthorizationDocument authorization,
        RunCaseSelection[] selection)
    {
        var policy = plan.Policy;
        var policyModelCalls = policy.GetProperty("maximumModelCalls").GetInt64();
        var policyHttpRequests = policy.GetProperty("maximumHttpRequests").GetInt64();
        var requiredModelCalls = selection.Sum(s => (long)s.CaseElement.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32());
        var requiredHttpRequests = selection.Sum(s => (long)s.CaseElement.GetProperty("maximumPhysicalWireAttemptsPerSlot").GetInt32());
        var requiredInput = 0L;
        var requiredOutput = 0L;
        var requiredReasoning = 0L;
        var requiredTotal = 0L;
        var requiredCost = 0m;
        string? currency = null;
        foreach (var selected in selection)
        {
            var reservation = selected.CaseElement.GetProperty("requestedReservation");
            if (reservation.ValueKind != JsonValueKind.Object
                || NullableLong(reservation, "inputTokens") is not { } input
                || NullableLong(reservation, "outputTokens") is not { } output
                || NullableLong(reservation, "reasoningTokens") is not { } reasoning
                || NullableLong(reservation, "totalTokens") is not { } total
                || NullableString(reservation, "maximumCost") is not { } cost
                || NullableString(reservation, "currency") is not { } caseCurrency)
                throw new ExecutionException("live-authorization-bound-unknown");
            requiredInput = checked(requiredInput + input);
            requiredOutput = checked(requiredOutput + output);
            requiredReasoning = checked(requiredReasoning + reasoning);
            requiredTotal = checked(requiredTotal + total);
            requiredCost += decimal.Parse(cost, CultureInfo.InvariantCulture);
            if (currency is not null && currency != caseCurrency)
                throw new ExecutionException("live-authorization-budget-mismatch");
            currency = caseCurrency;
        }
        var budget = authorization.Budget;
        if (budget.ModelCalls < requiredModelCalls || budget.HttpRequests < requiredHttpRequests
            || budget.ModelCalls > policyModelCalls || budget.HttpRequests > policyHttpRequests
            || budget.InputTokens is not { } inputCeiling || inputCeiling < requiredInput
            || budget.OutputTokens is not { } outputCeiling || outputCeiling < requiredOutput
            || budget.ReasoningTokens is not { } reasoningCeiling || reasoningCeiling < requiredReasoning
            || budget.TotalTokens is not { } totalCeiling || totalCeiling < requiredTotal
            || budget.MaximumCost is null || budget.Currency is null
            || decimal.Parse(budget.MaximumCost, CultureInfo.InvariantCulture) < requiredCost
            || budget.Currency != currency)
            throw new ExecutionException("live-authorization-budget-mismatch");
        var policyCost = NullableString(policy, "maximumEstimatedCost");
        var policyCurrency = NullableString(policy, "currency");
        if (policyCost is not null && policyCurrency is not null
            && (budget.Currency != policyCurrency
                || decimal.Parse(budget.MaximumCost, CultureInfo.InvariantCulture) > decimal.Parse(policyCost, CultureInfo.InvariantCulture)))
            throw new ExecutionException("live-authorization-budget-mismatch");
    }

    private static bool SameSelection(RunCaseSelection[] left, RunCaseSelection[] right) =>
        left.Length == right.Length
        && left.Select(s => s.CaseId + "|" + s.TrialId).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Select(s => s.CaseId + "|" + s.TrialId).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool Reference(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-');

    private static bool Sha256Reference(string value) => !string.IsNullOrEmpty(value) && value.Length == 71
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static long? NullableLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal static class LiveAuthorizationAuthentication
{
    public const string VerificationKeyEnvironmentVariable = "BAIZE_LIVE_AUTHORIZATION_PUBLIC_KEY";

    public static LiveAuthorizationDocument Sign(LiveAuthorizationDocument document, ECDsa signingKey)
    {
        if (signingKey.KeySize != 256)
            throw new ArgumentException("Authorization signatures require ECDSA P-256.", nameof(signingKey));
        var unsigned = document with { Signature = string.Empty };
        var signature = signingKey.SignData(CanonicalJson.Write(unsigned), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return unsigned with { Signature = Convert.ToBase64String(signature) };
    }

    public static void Verify(LiveAuthorizationDocument document)
    {
        byte[] publicKey;
        try
        {
            var configured = Environment.GetEnvironmentVariable(VerificationKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured)) throw new ExecutionException("live-authorization-key-unavailable");
            publicKey = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new ExecutionException("live-authorization-key-unavailable");
        }

        using var verifier = ECDsa.Create();
        try
        {
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || verifier.KeySize != 256)
                throw new ExecutionException("live-authorization-key-unavailable");
            var unsigned = document with { Signature = string.Empty };
            byte[] actual;
            try { actual = Convert.FromBase64String(document.Signature); }
            catch (Exception exception) when (exception is FormatException or ArgumentNullException)
            { throw new ExecutionException("live-authorization-signature-invalid"); }
            if (actual.Length != 64 || !verifier.VerifyData(CanonicalJson.Write(unsigned), actual,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new ExecutionException("live-authorization-signature-invalid");
        }
        catch (ExecutionException) { throw; }
        catch (CryptographicException)
        {
            throw new ExecutionException("live-authorization-key-unavailable");
        }
    }
}
