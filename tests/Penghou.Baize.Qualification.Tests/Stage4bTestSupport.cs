using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

internal static class Stage4bAuthorizations
{
    public const string TestAuthorityId = "11111111111111111111111111111111";
    private static readonly byte[] TestPrivateKey = CreateTestPrivateKey();
    private static readonly string TestPublicKey = CreateTestPublicKey();

    public static LiveAuthorizationDocument Create(FrozenRunPlan plan, RunCaseSelection[] selection, string artifactDestination,
        string authorizationRef = "stage4b-test-authorization-1", string operatorRef = "stage4b-test-operator")
    {
        var first = selection[0];
        var binding = first.SubjectElement.GetProperty("binding");
        var resolved = binding.GetProperty("resolved");
        var requested = binding.GetProperty("requested");
        var endpoint = requested.GetProperty("endpoint");
        var transport = resolved.GetProperty("transport");
        var cases = selection.Select(selected =>
        {
            var planCase = selected.CaseElement;
            return new LiveAuthorizationCase(
                selected.CaseId,
                selected.TrialId,
                selected.TrialSlot,
                selected.ModelId,
                selected.ScenarioId,
                planCase.GetProperty("observations").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToArray(),
                planCase.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32(),
                planCase.GetProperty("maximumPhysicalWireAttemptsPerSlot").GetInt32());
        }).ToArray();
        var budget = BudgetFor(selection);
        var settings = resolved.GetProperty("settings").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var document = new LiveAuthorizationDocument(
            LiveAuthorizations.Kind,
            LiveAuthorizations.SchemaVersion,
            Guid.NewGuid().ToString("N"),
            TestAuthorityId,
            CanonicalJson.Identity(new { kind = "qualification-live-run-proposal-test", planHash = plan.PlanHash }),
            string.Empty,
            authorizationRef,
            operatorRef,
            now - TimeSpan.FromMinutes(1),
            now + TimeSpan.FromMinutes(5),
            plan.PlanHash,
            plan.ContractRegistryHash,
            plan.Document.GetProperty("source").GetProperty("inventoryHash").GetString()!,
            plan.Document.GetProperty("source").GetProperty("policyHash").GetString()!,
            transport.GetProperty("profileId").GetString()!,
            transport.GetProperty("identity").GetString()!,
            resolved.GetProperty("provider").GetString()!,
            resolved.GetProperty("endpointId").GetString()!,
            resolved.GetProperty("baseUrl").GetString()!,
            resolved.GetProperty("origin").GetString()!,
            resolved.GetProperty("providerModel").GetString()!,
            requested.GetProperty("protocol").GetString()!,
            requested.TryGetProperty("apiVersion", out var apiVersion) && apiVersion.ValueKind == JsonValueKind.String
                ? apiVersion.GetString() : null,
            endpoint.GetProperty("apiKeySecretName").GetString()!,
            artifactDestination,
            settings,
            cases,
            budget);
        return Sign(document);
    }

    public static string Write(Stage3Workspace workspace, LiveAuthorizationDocument document, string name = "authorization.json")
    {
        document = Sign(document);
        var path = workspace.WriteInput(name, System.Text.Encoding.UTF8.GetString(CanonicalJson.Write(document)));
        return path;
    }

    public static LiveAuthorizationDocument Sign(LiveAuthorizationDocument document)
    {
        Environment.SetEnvironmentVariable(LiveAuthorizationAuthentication.VerificationKeyEnvironmentVariable, TestPublicKey);
        using var signingKey = ECDsa.Create();
        signingKey.ImportPkcs8PrivateKey(TestPrivateKey, out _);
        return LiveAuthorizationAuthentication.Sign(document, signingKey);
    }

    private static byte[] CreateTestPrivateKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKey();
    }

    private static string CreateTestPublicKey()
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(TestPrivateKey, out _);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static RunCaseSelection[] Select(FrozenRunPlan plan, int caseIndex = 0, int trialIndex = 0)
    {
        var caseElement = plan.Cases[caseIndex];
        var subjectId = caseElement.GetProperty("subjectId").GetString()!;
        var subject = plan.Subjects.Single(s => s.GetProperty("id").GetString() == subjectId);
        var trial = caseElement.GetProperty("trials")[trialIndex];
        return
        [
            new RunCaseSelection(caseElement.GetProperty("id").GetString()!, subjectId,
                caseElement.GetProperty("modelId").GetString()!, caseElement.GetProperty("scenario").GetString()!,
                caseElement, subject, trial.GetProperty("id").GetString()!, trial.GetProperty("slot").GetInt32())
        ];
    }

    public static BudgetEnvelopeEvidence BudgetFor(RunCaseSelection[] selection)
    {
        var modelCalls = selection.Sum(s => (long)s.CaseElement.GetProperty("maximumSemanticInvocationsPerSlot").GetInt32());
        var httpRequests = selection.Sum(s => (long)s.CaseElement.GetProperty("maximumPhysicalWireAttemptsPerSlot").GetInt32());
        var input = 0L;
        var output = 0L;
        var reasoning = 0L;
        var total = 0L;
        var cost = 0m;
        string? currency = null;
        var costKnown = true;
        foreach (var selected in selection)
        {
            var reservation = selected.CaseElement.GetProperty("requestedReservation");
            input += reservation.GetProperty("inputTokens").GetInt64();
            output += reservation.GetProperty("outputTokens").GetInt64();
            reasoning += reservation.GetProperty("reasoningTokens").GetInt64();
            total += reservation.GetProperty("totalTokens").GetInt64();
            if (reservation.TryGetProperty("maximumCost", out var costElement) && costElement.ValueKind == JsonValueKind.String)
                cost += decimal.Parse(costElement.GetString()!, CultureInfo.InvariantCulture);
            else
                costKnown = false;
            currency ??= reservation.TryGetProperty("currency", out var currencyElement)
                && currencyElement.ValueKind == JsonValueKind.String ? currencyElement.GetString() : null;
        }
        return new BudgetEnvelopeEvidence(modelCalls, httpRequests, input, output, reasoning, total,
            costKnown ? cost.ToString("0.#############################", CultureInfo.InvariantCulture) : null,
            costKnown ? currency : null);
    }
}
