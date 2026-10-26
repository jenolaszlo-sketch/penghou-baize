using System.Text;
using Penghou.Baize.Qualification.Planning;
using Penghou.Siming;

namespace Penghou.Baize.Qualification.Execution;

// Offline report generation from recorded evidence only. It opens the ledger,
// verifies the hash chain (against the captured checkpoint when present) and
// writes JSON + Markdown. It performs no execution and no secret resolution.
internal static class ReportCommand
{
    public static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || args[i] != "--run" || !options.TryAdd("--run", args[i + 1]))
                    throw new ExecutionException("invalid-options");
            }
            if (!options.ContainsKey("--run")) throw new ExecutionException("missing-options");
            var runDirectory = Path.GetFullPath(options["--run"]);
            if (!Directory.Exists(runDirectory)) throw new ExecutionException("run-directory-missing");
            var manifest = RunManifest.Read(runDirectory);
            var plan = FrozenRunPlan.Load(Path.Combine(runDirectory, "plan.json"));
            if (plan.PlanHash != manifest.PlanHash) throw new ExecutionException("manifest-plan-mismatch");
            var selection = manifest.Cases.Select(binding => new RunCaseSelection(binding.CaseId, binding.SubjectId,
                binding.ModelId, binding.ScenarioId,
                plan.Cases.Single(c => c.GetProperty("id").GetString() == binding.CaseId),
                plan.Subjects.Single(s => s.GetProperty("id").GetString() == binding.SubjectId),
                binding.TrialId, binding.TrialSlot)).ToArray();
            var workflowStatuses = await ReadWorkflowStatusesAsync(runDirectory, manifest, cancellationToken).ConfigureAwait(false);
            var checkpointCaptured = false;
            LedgerVerificationResult verification;
            await using (var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), ExecutionFaults.None))
            {
                var checkpointPath = Path.Combine(runDirectory, "checkpoint.json");
                if (File.Exists(checkpointPath))
                {
                    var checkpoint = LedgerCheckpoints.Import(File.ReadAllBytes(checkpointPath));
                    checkpointCaptured = true;
                    verification = await ledger.VerifyAsync(checkpoint, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    verification = await ledger.VerifyAsync(null, cancellationToken).ConfigureAwait(false);
                }
            }
            var report = ReportWriter.BuildReportForCommand(runDirectory, manifest.RunId, plan, selection,
                verification, checkpointCaptured, workflowStatuses, cancellationToken);
            var reports = Path.Combine(runDirectory, "reports");
            Directory.CreateDirectory(reports);
            File.WriteAllBytes(Path.Combine(reports, "summary.json"), CanonicalJson.Write(new
            {
                kind = "qualification-report",
                schemaVersion = "1.0",
                report,
                generatedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            }));
            File.WriteAllBytes(Path.Combine(reports, "report.md"), Encoding.UTF8.GetBytes(ReportMarkdown.Build(report)));
            var exitCode = ComputeExitCode(report);
            output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
            {
                kind = "report-summary",
                schemaVersion = "1.0",
                runId = manifest.RunId,
                planHash = manifest.PlanHash,
                commandCompleted = true,
                qualificationPerformed = false,
                ledgerVerified = verification.IsValid,
                checkpointVerified = report.CheckpointVerified,
                complete = report.Complete,
                outcome = exitCode switch
                {
                    0 => "all-selected-cases-passed",
                    3 => "contract-failure",
                    4 => "blocked-or-incomplete",
                    _ => "failure"
                },
                exitCode
            })));
            return exitCode;
        }
        catch (PlanningException exception) { return Fail(exception.Code, 2, output, error); }
        catch (ExecutionException exception) { return Fail(exception.Code, 2, output, error); }
        catch (OperationCanceledException) { return Fail("operator-cancellation", 130, output, error); }
        catch (Exception) { return Fail("evidence-or-internal-failure", 5, output, error); }
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> ReadWorkflowStatusesAsync(string runDirectory,
        RunManifestRecord manifest, CancellationToken cancellationToken)
    {
        var statuses = new Dictionary<Guid, string>();
        try
        {
            var store = new Penghou.Zhinu.Sqlite.SqliteWorkflowStore(new Penghou.Zhinu.Sqlite.ZhinuSqliteOptions
            {
                DatabasePath = Path.Combine(runDirectory, "workflow.db")
            });
            await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var registry = new Penghou.Zhinu.WorkflowRegistry().Register<CaseExecutionRequest, TrialOutcomeRecord>(
                QualificationWorkflow.Name, QualificationWorkflow.Version,
                new QualificationWorkflow(new WorkflowServices(null!, null!, ExecutionFaults.None, TimeProvider.System)));
            var engine = new Penghou.Zhinu.WorkflowEngine(store, registry,
                new Penghou.Zhinu.ZhinuOptions { MaxConcurrentWorkflows = 1 });
            foreach (var binding in manifest.Cases)
            {
                var run = await engine.GetRunAsync(Guid.Parse(binding.WorkflowRunId), cancellationToken).ConfigureAwait(false);
                if (run is not null) statuses[Guid.Parse(binding.WorkflowRunId)] = run.Status.ToString();
            }
            await engine.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A broken workflow store is reported as unknown workflow status;
            // the evidence remains authoritative.
        }
        return statuses;
    }

    private static int ComputeExitCode(ReportRun report)
    {
        if (!report.LedgerVerified) return 5;
        if (!report.Complete) return 4;
        if (report.Blocked > 0 || report.Cancelled > 0 || report.Inconclusive > 0 || report.NotRun > 0) return 4;
        if (report.Failed > 0) return 3;
        return 0;
    }

    private static int Fail(string code, int exitCode, TextWriter output, TextWriter error)
    {
        error.WriteLine(code);
        output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
        {
            kind = "command-failed",
            schemaVersion = "1.0",
            code,
            exitCode,
            commandCompleted = false,
            qualificationPerformed = false,
            executionAuthorized = false
        })));
        return exitCode;
    }
}

internal static class ReportMarkdown
{
    public static string Build(ReportRun report)
    {
        var builder = new StringBuilder();
        builder.Append("# Qualification report\n\n");
        builder.Append($"Run `{report.RunId}` — plan `{report.PlanHash}` — source `{report.SourceRevision}`\n\n");
        builder.Append($"Evidence canonicalization: `{report.EvidenceCanonicalization}`\n\n");
        builder.Append($"Ledger verified: **{(report.LedgerVerified ? "yes" : "no")}**; checkpoint verified: **{(report.CheckpointVerified ? "yes" : "no")}**; complete: **{(report.Complete ? "yes" : "no")}**\n\n");
        builder.Append($"Passed {report.Passed}, Failed {report.Failed}, Blocked {report.Blocked}, Cancelled {report.Cancelled}, Inconclusive {report.Inconclusive}, NotRun {report.NotRun}\n\n");
        builder.Append($"Budget consumed: {report.ConsumedModelCalls} model call(s), {report.ConsumedHttpRequests} HTTP request(s). Unresolved liability: {report.UnresolvedLiability ?? "none"}\n\n");
        builder.Append($"Live authorized: **{(report.LiveAuthorized ? "yes" : "no")}**; authorization `{report.LiveAuthorizationRef ?? "none"}` (`{report.LiveAuthorizationHash ?? "no-hash"}`)\n\n");
        builder.Append($"Execution boundary: authorization claims {report.ExecutionBoundary.AuthorizationClaimCount}; Baize dispatch admissions {report.ExecutionBoundary.BaizeDispatchCount}; client transport attempts recorded {report.ExecutionBoundary.ClientTransportAttemptCount} ({(report.ExecutionBoundary.ClientTransportAttemptCountComplete ? "complete" : "incomplete")}); observed HTTP responses {report.ExecutionBoundary.ObservedHttpResponseCount}. Configured model(s): `{string.Join(", ", report.ExecutionBoundary.ConfiguredProviderModels)}`; provider-reported model/route: `{report.ExecutionBoundary.ProviderReportedModelOrRoute ?? "not recorded"}`; provider-side execution count: **{report.ExecutionBoundary.ProviderExecutionCount}**. This is a client-side attempt bound, not an exactly-once provider-processing guarantee.\n\n");
        foreach (var reportCase in report.Cases)
        {
            builder.Append($"## {reportCase.ModelId} — {reportCase.ScenarioId} (trial {reportCase.TrialSlot})\n\n");
            builder.Append($"Outcome: **{reportCase.Outcome ?? "NotRun"}**; complete: {(reportCase.Complete ? "yes" : "no")}\n\n");
            builder.Append($"Hosting provider `{reportCase.HostingProvider}`, adapter `{reportCase.Provider}`, provider model `{reportCase.ProviderModel}`, endpoint `{reportCase.EndpointId}`, protocol `{reportCase.Protocol}` (`{reportCase.ApiVersion ?? "unversioned"}`), transport `{reportCase.TransportProfile}`, profile `{reportCase.TransportProfileId ?? "unrecorded"}` (`{reportCase.TransportProfileIdentity ?? "no-identity"}`), physical-attempt bound `{reportCase.PhysicalAttemptUpperBound?.ToString() ?? "unknown"}`, native tools `{(reportCase.NativeToolCalling ? "enabled" : "disabled")}`\n\n");
            builder.Append($"Contract `{reportCase.ScenarioId}`; observed `{string.Join(", ", reportCase.ObservedContractIds)}`; fixture `{reportCase.FixtureId}` (`{reportCase.FixtureHash}`)\n\n");
            if (reportCase.TransportChain is not null || reportCase.WireAttemptBound is not null)
                builder.Append($"Transport chain: `{reportCase.TransportChain ?? "unrecorded"}`; wire-attempt bound: `{reportCase.WireAttemptBound ?? "unknown"}`\n\n");
            if (reportCase.EligibilityState is not null)
            {
                builder.Append($"Live eligibility: **{reportCase.EligibilityState}**\n");
                foreach (var reason in reportCase.EligibilityCriticalReasons)
                    builder.Append($"- Critical: {reason}\n");
                foreach (var limitation in reportCase.EligibilityLimitations)
                    builder.Append($"- Eligibility limitation: {limitation}\n");
                builder.Append('\n');
            }
            if (reportCase.FailedAssertionIds.Length > 0)
                builder.Append($"Failed assertions: {string.Join(", ", reportCase.FailedAssertionIds)}\n\n");
            if (reportCase.FailureCategory is not null || reportCase.FailureStage is not null
                || reportCase.SuggestedAttributionClass is not null)
                builder.Append($"Failure category: `{reportCase.FailureCategory ?? "none"}`; stage: `{reportCase.FailureStage ?? "unknown"}`; suggested attribution: `{reportCase.SuggestedAttributionClass ?? "unverified"}`; recorded attribution: `{reportCase.Attribution}`\n\n");
            foreach (var limitation in reportCase.Limitations)
                builder.Append($"- Limitation: {limitation}\n");
            builder.Append('\n');
            foreach (var attempt in reportCase.Attempts)
            {
                builder.Append($"- Attempt `{attempt.AttemptId}`: {attempt.Outcome ?? "unresolved"}");
                if (!attempt.ModelInvoked && attempt.DenialCode is not null) builder.Append($" (denied: {attempt.DenialCode})");
                if (attempt.Interrupted) builder.Append(" (interrupted)");
                if (attempt.Released) builder.Append(" (released, not dispatched)");
                builder.Append($"; HTTP dispatches {attempt.HttpDispatches}, last status {attempt.LastHttpStatus?.ToString() ?? "none"}");
                builder.Append($"; usage {attempt.UsageCompleteness ?? "none"}, settlement {attempt.Settlement ?? "none"}\n");
                foreach (var reference in attempt.Evidence)
                    builder.Append($"  - `{reference.EventType}` sequence {reference.Sequence} hash {reference.Hash}\n");
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
