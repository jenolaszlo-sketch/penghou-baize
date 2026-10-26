using System.Text.Json;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

// The immutable run binding: everything a resume must re-verify before any
// secret reference or invocation. The Siming ledger and the Zhinu store hold
// the authoritative history; the manifest only binds identities.
internal sealed record RunManifestRecord(
    string SchemaVersion,
    string RunId,
    string CreatedUtc,
    string PlanHash,
    string ContractRegistryHash,
    string SourceRevision,
    string Framework,
    string ZhinuComponent,
    string SimingComponent,
    ManifestCaseBinding[] Cases,
    string? LiveAuthorizationRef = null,
    string? LiveAuthorizationHash = null,
    bool LiveAuthorized = false,
    string? LiveAuthorizationId = null);

internal sealed record ManifestCaseBinding(
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string SubjectId,
    string WorkflowRunId);

internal static class RunManifest
{
    public const string FileName = "manifest.json";

    public static RunManifestRecord Create(FrozenRunPlan plan, string runId, ManifestCaseBinding[] cases,
        string? liveAuthorizationRef = null, string? liveAuthorizationHash = null, bool liveAuthorized = false,
        string? liveAuthorizationId = null) => new(
        "1.0", runId, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        plan.PlanHash, plan.ContractRegistryHash, plan.SourceRevision, plan.Framework,
        "Penghou.Zhinu 0.2.0-preview.4 + Penghou.Zhinu.Sqlite 0.2.0-preview.4",
        "Penghou.Siming 1.0.0 + Penghou.Siming.Sqlite 1.0.0", cases,
        liveAuthorizationRef, liveAuthorizationHash, liveAuthorized, liveAuthorizationId);

    public static void Write(string runDirectory, RunManifestRecord manifest)
    {
        File.WriteAllBytes(Path.Combine(runDirectory, FileName),
            CanonicalJson.WriteElement(JsonSerializer.SerializeToElement(manifest, CanonicalJson.Options)));
    }

    public static RunManifestRecord Read(string runDirectory)
    {
        var path = Path.Combine(runDirectory, FileName);
        if (!File.Exists(path)) throw new ExecutionException("run-manifest-missing");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var manifest = document.RootElement.Deserialize<RunManifestRecord>(CanonicalJson.Options)
            ?? throw new ExecutionException("run-manifest-invalid");
        if (manifest.SchemaVersion != "1.0") throw new ExecutionException("run-manifest-invalid");
        return manifest;
    }
}
