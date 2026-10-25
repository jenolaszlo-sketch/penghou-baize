using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Planning;
using Penghou.Siming;
using Penghou.Zhinu;

namespace Penghou.Baize.Qualification.Execution;

internal sealed record RunCaseSelection(string CaseId, string SubjectId, string ModelId, string ScenarioId,
    JsonElement CaseElement, JsonElement SubjectElement, string TrialId, int TrialSlot);

internal sealed record CaseOutcome(string CaseId, string TrialId, int TrialSlot, string ModelId, string ScenarioId,
    AttemptOutcome Outcome, string[] FailedAssertionIds, string? FailureCategory, string? Attribution, bool Complete,
    string Status);

internal sealed class LiveEligibilityException(string[] criticalReasons, string[] limitations) : Exception("Live execution is not eligible for the selected cases.")
{
    public string[] CriticalReasons { get; } = criticalReasons;
    public string[] Limitations { get; } = limitations;
}

internal sealed class RunCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, ExecutionFaults faults,
        CancellationToken cancellationToken)
    {
        var run = new RunCommand(output, error, faults);
        return await run.ExecuteRunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> ResumeAsync(string[] args, TextWriter output, TextWriter error, ExecutionFaults faults,
        CancellationToken cancellationToken)
    {
        var run = new RunCommand(output, error, faults);
        return await run.ExecuteResumeAsync(args, cancellationToken).ConfigureAwait(false);
    }

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ExecutionFaults _faults;

    private RunCommand(TextWriter output, TextWriter error, ExecutionFaults faults)
    {
        _output = output;
        _error = error;
        _faults = faults;
    }

    private async Task<int> ExecuteRunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = ParseOptions(args, new[] { "--plan", "--out", "--models", "--contracts" }, optional: "--trial",
                bareFlags: ["--allow-live"]);
            var allowLive = options.ContainsKey("--allow-live");
            var plan = FrozenRunPlan.Load(options["--plan"]);
            var selection = SelectCases(plan, options);
            foreach (var selected in selection)
            {
                var transportProfile = selected.SubjectElement.GetProperty("binding").GetProperty("requested")
                    .GetProperty("transportProfile").GetString();
                if (transportProfile == "SyntheticFake") continue;
                if (!allowLive) throw new ExecutionException("real-network-not-authorized");
                var assessment = LiveEligibility.Assess(plan, selected, Guid.Empty.ToString("N"));
                if (assessment.State != LiveEligibility.StateEligible)
                    throw new LiveEligibilityException(assessment.CriticalReasons, assessment.Limitations);
            }
            var runDirectory = Path.GetFullPath(options["--out"]);
            if (Directory.Exists(runDirectory) || File.Exists(runDirectory))
                throw new ExecutionException("run-output-already-exists");
            Directory.CreateDirectory(runDirectory);
            Directory.CreateDirectory(Path.Combine(runDirectory, "reports"));
            var runId = Guid.NewGuid().ToString("N");
            var manifestCases = selection.Select(s => new ManifestCaseBinding(s.CaseId, s.TrialId, s.TrialSlot, s.ModelId,
                s.ScenarioId, s.SubjectId, QualificationWorkflow.StableRunId(plan.PlanHash, s.CaseId, s.TrialId).ToString("N"))).ToArray();
            var manifest = RunManifest.Create(plan, runId, manifestCases);
            File.WriteAllBytes(Path.Combine(runDirectory, "plan.json"), plan.PlanBytes);
            var identityFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(plan.PlanFile))!, "identity.json");
            File.Copy(identityFile, Path.Combine(runDirectory, "identity.json"));
            RunManifest.Write(runDirectory, manifest);
            return await ExecuteCasesAsync(plan, manifest, selection, runDirectory, cancellationToken,
                resume: false).ConfigureAwait(false);
        }
        catch (LiveEligibilityException exception) { return FailEligibility(exception); }
        catch (PlanningException exception) { return Fail(exception.Code, 2); }
        catch (ExecutionException exception) { return Fail(exception.Code, 2); }
        catch (SimulatedProcessCrashException) { throw; }
        catch (OperationCanceledException) { return Fail("operator-cancellation", 130); }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException
            or OverflowException or FileNotFoundException or DirectoryNotFoundException)
        { return Fail("invalid-authoritative-input", 2); }
        catch (Exception exception)
        {
            if (Environment.GetEnvironmentVariable("BAIZE_QUALIFICATION_DEBUG") == "1")
                _error.WriteLine(exception.ToString());
            _error.WriteLine("diagnostic:" + exception.GetType().Name);
            return Fail("evidence-or-internal-failure", 5);
        }
    }

    private async Task<int> ExecuteResumeAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = ParseOptions(args, new[] { "--run" }, optional: null);
            var runDirectory = Path.GetFullPath(options["--run"]);
            if (!Directory.Exists(runDirectory)) throw new ExecutionException("run-directory-missing");
            var manifest = RunManifest.Read(runDirectory);
            var planFile = Path.Combine(runDirectory, "plan.json");
            var plan = FrozenRunPlan.Load(planFile);
            if (plan.PlanHash != manifest.PlanHash) throw new ExecutionException("manifest-plan-mismatch");
            var selection = SelectCases(plan, new Dictionary<string, string>(StringComparer.Ordinal), manifest);
            return await ExecuteCasesAsync(plan, manifest, selection, runDirectory, cancellationToken,
                resume: true).ConfigureAwait(false);
        }
        catch (PlanningException exception) { return Fail(exception.Code, 2); }
        catch (ExecutionException exception) { return Fail(exception.Code, 2); }
        catch (SimulatedProcessCrashException) { throw; }
        catch (OperationCanceledException) { return Fail("operator-cancellation", 130); }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException
            or OverflowException or FileNotFoundException or DirectoryNotFoundException)
        { return Fail("invalid-authoritative-input", 2); }
        catch (Exception exception)
        {
            if (Environment.GetEnvironmentVariable("BAIZE_QUALIFICATION_DEBUG") == "1")
                _error.WriteLine(exception.ToString());
            _error.WriteLine("diagnostic:" + exception.GetType().Name);
            return Fail("evidence-or-internal-failure", 5);
        }
    }

    private Dictionary<string, string> ParseOptions(string[] args, string[] required, string? optional,
        string[]? bareFlags = null)
    {
        var allowed = required.Concat(optional is null ? [] : new[] { optional })
            .Concat(bareFlags ?? []).ToArray();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (bareFlags is not null && bareFlags.Contains(args[i], StringComparer.Ordinal))
            {
                if (!options.TryAdd(args[i], "true")) throw new ExecutionException("invalid-options");
                continue;
            }
            if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(args[i + 1]) || !options.TryAdd(args[i], args[i + 1]))
                throw new ExecutionException("invalid-options");
            i++;
        }
        foreach (var requiredOption in required)
            if (!options.ContainsKey(requiredOption)) throw new ExecutionException("missing-options");
        return options;
    }

    private static RunCaseSelection[] SelectCases(FrozenRunPlan plan, Dictionary<string, string> options,
        RunManifestRecord? manifest = null)
    {
        if (manifest is null)
        {
            var models = Split(options["--models"]);
            var contracts = Split(options["--contracts"]);
            foreach (var model in models)
                if (!plan.Models.Contains(model, StringComparer.Ordinal)) throw new ExecutionException("unknown-model");
            foreach (var contract in contracts)
                if (ContractRegistry.Current.All(c => c.Id != contract)) throw new ExecutionException("unknown-or-historical-contract");
            var trial = options.TryGetValue("--trial", out var trialValue) ? ParseTrial(trialValue) : 1;
            var selected = new List<RunCaseSelection>();
            foreach (var caseElement in plan.Cases)
            {
                var modelId = caseElement.GetProperty("modelId").GetString()!;
                var scenarioId = caseElement.GetProperty("scenario").GetString()!;
                if (!models.Contains(modelId, StringComparer.Ordinal) || !contracts.Contains(scenarioId, StringComparer.Ordinal))
                    continue;
                if (caseElement.GetProperty("applicability").GetString() != "Applicable")
                    throw new ExecutionException("excluded-case-cannot-execute");
                var subjectId = caseElement.GetProperty("subjectId").GetString()!;
                var subject = plan.Subjects.Single(s => s.GetProperty("id").GetString() == subjectId);
                var trials = caseElement.GetProperty("trials").EnumerateArray().ToArray();
                if (trial < 1 || trial > trials.Length) throw new ExecutionException("trial-out-of-range");
                var trialElement = trials[trial - 1];
                selected.Add(new RunCaseSelection(caseElement.GetProperty("id").GetString()!, subjectId, modelId, scenarioId,
                    caseElement, subject, trialElement.GetProperty("id").GetString()!, trialElement.GetProperty("slot").GetInt32()));
            }
            if (selected.Count == 0) throw new ExecutionException("empty-or-unknown-selection");
            return selected.ToArray();
        }
        return manifest.Cases.Select(binding => new RunCaseSelection(binding.CaseId, binding.SubjectId, binding.ModelId,
            binding.ScenarioId,
            plan.Cases.Single(c => c.GetProperty("id").GetString() == binding.CaseId),
            plan.Subjects.Single(s => s.GetProperty("id").GetString() == binding.SubjectId),
            binding.TrialId, binding.TrialSlot)).ToArray();
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.TrimEntries);
    private static int ParseTrial(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var trial) ? trial : -1;

    private async Task<int> ExecuteCasesAsync(FrozenRunPlan plan, RunManifestRecord manifest,
        RunCaseSelection[] selection, string runDirectory, CancellationToken cancellationToken, bool resume)
    {
        var planInventory = plan.Inventory;
        var environment = await ExecutionEnvironment.CreateAsync(runDirectory, _faults,
            DeserializeInventory(planInventory), BuildBudget(plan), cancellationToken).ConfigureAwait(false);
        await using (environment)
        {
            var outcomes = new List<CaseOutcome>();
            var interrupted = false;
            var cancelled = false;
            var internalFailure = false;
            var restarted = new HashSet<Guid>();
            var selectionSummary = selection.Select(s => new SelectedCaseEvidence(s.CaseId, s.ModelId, s.ScenarioId,
                ObservedContracts(s.CaseElement), s.TrialSlot, s.TrialId)).ToArray();
            try
            {
                var source = plan.Document.GetProperty("source");
                await environment.Ledger.AppendAsync(EvidenceLedger.RunStream, EvidenceLedger.RunStarted,
                    new RunStartedEvidence("1.0", manifest.RunId, manifest.PlanHash, manifest.ContractRegistryHash,
                        source.GetProperty("inventoryHash").GetString()!, source.GetProperty("policyHash").GetString()!,
                        manifest.SourceRevision, manifest.Framework, manifest.ZhinuComponent, manifest.SimingComponent,
                        manifest.CreatedUtc, selectionSummary),
                    idempotencyKey: "run-started", cancellationToken: cancellationToken).ConfigureAwait(false);
                _faults.AfterRunStartedAppend?.Invoke();
                // The eligibility assessment is evidence, never authorization.
                foreach (var selected in selection)
                {
                    var assessment = LiveEligibility.Assess(plan, selected, manifest.RunId);
                    await environment.Ledger.AppendAsync(
                        EvidenceLedger.TrialStream(selected.CaseId, selected.TrialSlot),
                        EvidenceLedger.EligibilityAssessed, assessment,
                        idempotencyKey: "eligibility-" + manifest.RunId + "-" + selected.CaseId + "-" + selected.TrialSlot,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _faults.Interrupt?.Token ?? CancellationToken.None);
                foreach (var selected in selection)
                {
                    var request = BuildRequest(plan, selected, manifest.RunId);
                    var stableId = QualificationWorkflow.StableRunId(plan.PlanHash, selected.CaseId, selected.TrialId);
                    var deadline = DateTimeOffset.UtcNow + TimeSpan.Parse(plan.Policy.GetProperty("maximumDuration").GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture);
                    var handle = await environment.Engine.StartHandleAsync<CaseExecutionRequest, TrialOutcomeRecord>(
                        QualificationWorkflow.Name, QualificationWorkflow.Version, request, stableId, deadline,
                        cancellationToken: executionCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        await environment.Engine.ExecuteAsync(stableId, executionCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_faults.Interrupt is { IsCancellationRequested: true }
                        && !cancellationToken.IsCancellationRequested)
                    {
                        // Simulated process death: the durable run remains
                        // Running with nothing committed for the in-flight
                        // step. Nothing else may execute in this process.
                        throw new SimulatedProcessCrashException("workflow-interrupted");
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        await environment.Engine.CancelAsync(stableId, "operator", "cli-cancellation",
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception) when (!cancelled)
                    {
                        interrupted = true;
                    }
                    var snapshot = await handle.GetResultAsync(CancellationToken.None).ConfigureAwait(false);
                    if (!snapshot.IsTerminal && !cancelled)
                    {
                        // Zhinu swallowed the worker cancellation and released
                        // the lease; the run remains resumable. A simulated
                        // crash must not continue executing in this process.
                        if (_faults.Interrupt is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
                            throw new SimulatedProcessCrashException("workflow-interrupted");
                        if (cancellationToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            await environment.Engine.CancelAsync(stableId, "operator", "cli-cancellation",
                                CancellationToken.None).ConfigureAwait(false);
                            snapshot = await handle.GetResultAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    if (snapshot.Status == WorkflowStatus.Completed && snapshot.Value is not null)
                    {
                        outcomes.Add(new CaseOutcome(selected.CaseId, selected.TrialId, selected.TrialSlot, selected.ModelId,
                            selected.ScenarioId, snapshot.Value.Outcome, snapshot.Value.FailedAssertionIds,
                            snapshot.Value.FailureCategory, snapshot.Value.Attribution, snapshot.Value.Complete, "Completed"));
                    }
                    else if (snapshot.Status == WorkflowStatus.Failed)
                    {
                        var type = snapshot.Error?.Type ?? "unknown";
                        var message = snapshot.Error?.Message ?? "";
                        if (Environment.GetEnvironmentVariable("BAIZE_QUALIFICATION_DEBUG") == "1")
                            _error.WriteLine("diagnostic-workflow:" + type);
                        if (type.Contains("ExecutionException", StringComparison.Ordinal))
                            return Fail("execution-binding-failed", 2);
                        if (resume && !cancelled && IsRecoverable(type, message) && !restarted.Contains(stableId))
                        {
                            // Zhinu administrative restart: only infrastructure
                            // failures re-execute, always as a distinct attempt
                            // with fresh evidence. Contract outcomes are never
                            // restarted (they complete as failed trials).
                            restarted.Add(stableId);
                            var failedStep = (await handle.GetStepsAsync(CancellationToken.None).ConfigureAwait(false))
                                .FirstOrDefault(s => s.Status == StepStatus.Failed);
                            if (failedStep is not null)
                            {
                                await environment.Engine.RestartStepAsync(stableId, failedStep.StepKey,
                                    new RestartStepOptions
                                    {
                                        Mode = StepRestartMode.Dependents,
                                        OperationId = RestartOperationId(manifest.RunId, selected.CaseId, selected.TrialSlot),
                                        Actor = "runner",
                                        Reason = "recover-interrupted-qualification-attempt"
                                    }, CancellationToken.None).ConfigureAwait(false);
                                await environment.Engine.ExecuteAsync(stableId, CancellationToken.None).ConfigureAwait(false);
                                snapshot = await handle.GetResultAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        if (snapshot.Status == WorkflowStatus.Completed && snapshot.Value is not null)
                        {
                            outcomes.Add(new CaseOutcome(selected.CaseId, selected.TrialId, selected.TrialSlot, selected.ModelId,
                                selected.ScenarioId, snapshot.Value.Outcome, snapshot.Value.FailedAssertionIds,
                                snapshot.Value.FailureCategory, snapshot.Value.Attribution, snapshot.Value.Complete, "Completed"));
                        }
                        else
                        {
                            internalFailure = internalFailure || type.Contains("EvidenceIo", StringComparison.Ordinal)
                                || message.Contains("Evidence append failed", StringComparison.Ordinal);
                            outcomes.Add(new CaseOutcome(selected.CaseId, selected.TrialId, selected.TrialSlot, selected.ModelId,
                                selected.ScenarioId, AttemptOutcome.Inconclusive, [], null, null, false, snapshot.Status.ToString()));
                            interrupted = true;
                        }
                    }
                    else
                    {
                        outcomes.Add(new CaseOutcome(selected.CaseId, selected.TrialId, selected.TrialSlot, selected.ModelId,
                            selected.ScenarioId, AttemptOutcome.NotRun, [], null, null, false, snapshot.Status.ToString()));
                        interrupted = true;
                    }
                    if (cancelled) break;
                }
                _faults.AfterWorkflowsCompleted?.Invoke();
            }
            catch (Exception exception) when (exception is EvidenceIoException or SimulatedProcessCrashException)
            {
                throw;
            }

            if (!cancelled)
            {
                try
                {
                    var reconciliation = await Reconciliation.RunAsync(environment.Ledger, manifest.RunId,
                        environment.Time, cancellationToken).ConfigureAwait(false);
                    var reconciliationEvidence = new ReconciliationEvidence("1.0", manifest.RunId,
                        reconciliation.Interrupted, reconciliation.Released);
                    var reconciliationKey = "reconciliation-" + CanonicalJson.Hash(CanonicalJson.Write(reconciliationEvidence));
                    await environment.Ledger.AppendAsync(EvidenceLedger.RunStream, EvidenceLedger.Reconciliation,
                        reconciliationEvidence, idempotencyKey: reconciliationKey,
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            var budget = await BudgetLedger.ComputeAsync(environment.Ledger, BuildBudget(plan), CancellationToken.None).ConfigureAwait(false);
            if (!cancelled)
            {
                var summary = Summarize(outcomes);
                var runFinished = new RunFinishedEvidence("1.0", manifest.RunId, manifest.PlanHash,
                    !interrupted && summary.Complete && !internalFailure,
                    summary.Passed, summary.Failed, summary.Blocked, summary.Cancelled, summary.Inconclusive, summary.NotRun,
                    budget.ConsumedModelCalls, budget.ConsumedHttpRequests,
                    interrupted || internalFailure ? "unresolved-liability-retained" : null);
                // Digest-keyed so an unchanged summary deduplicates across
                // resume, while a state change appends a new superseding event.
                var finishedKey = "run-finished-" + CanonicalJson.Hash(CanonicalJson.Write(runFinished));
                await environment.Ledger.AppendAsync(EvidenceLedger.RunStream, EvidenceLedger.RunFinished,
                    runFinished, idempotencyKey: finishedKey, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            var verification = await environment.Ledger.VerifyAsync(null, CancellationToken.None).ConfigureAwait(false);
            var checkpoint = await CaptureCheckpointAsync(environment, runDirectory).ConfigureAwait(false);
            if (!verification.IsValid || internalFailure)
                return Fail("evidence-verification-or-storage-failure", 5);
            ReportWriter.Write(runDirectory, manifest.RunId, plan, selection, outcomes,
                verification, budget, checkpoint, _faults, CancellationToken.None);
            var exitCode = ComputeExitCode(outcomes, cancelled, interrupted, internalFailure);
            _output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
            {
                kind = "run-summary",
                schemaVersion = "1.0",
                runId = manifest.RunId,
                planHash = manifest.PlanHash,
                commandCompleted = exitCode is 0 or 3 or 4,
                qualificationPerformed = true,
                executionAuthorized = true,
                ledgerVerified = verification.IsValid,
                checkpointCaptured = checkpoint,
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
    }

    private static bool IsRecoverable(string failureType, string failureMessage) =>
        failureType.Contains("EvidenceIo", StringComparison.Ordinal)
        || failureMessage.Contains("Evidence append failed", StringComparison.Ordinal)
        || failureType.Contains("SimulatedProcessCrash", StringComparison.Ordinal)
        || failureType.Contains("OperationCanceled", StringComparison.Ordinal)
        || failureType.Contains("WorkflowTimeout", StringComparison.Ordinal)
        || failureType.Contains("WorkflowLease", StringComparison.Ordinal);

    private static Guid RestartOperationId(string runId, string caseId, int trialSlot)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            $"resume|{runId}|{caseId}|{trialSlot}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static Inventory DeserializeInventory(JsonElement inventoryElement)
    {
        using var document = JsonDocument.Parse(CanonicalJson.WriteElement(inventoryElement));
        return document.RootElement.Deserialize<Inventory>(CanonicalJson.Options)
            ?? throw new ExecutionException("invalid-plan-inventory");
    }

    private static BudgetEnvelopeEvidence BuildBudget(FrozenRunPlan plan)
    {
        var policy = plan.Policy;
        return new BudgetEnvelopeEvidence(policy.GetProperty("maximumModelCalls").GetInt64(),
            policy.GetProperty("maximumHttpRequests").GetInt64(), NullableLong(policy, "maximumInputTokens"),
            NullableLong(policy, "maximumOutputTokens"), NullableLong(policy, "maximumReasoningTokens"),
            NullableLong(policy, "maximumTotalTokens"), NullableString(policy, "maximumEstimatedCost"),
            NullableString(policy, "currency"));
    }

    private static long? NullableLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static CaseExecutionRequest BuildRequest(FrozenRunPlan plan, RunCaseSelection selected, string runId)
    {
        var policy = plan.Policy;
        var scenario = selected.CaseElement.GetProperty("observations").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == selected.ScenarioId);
        var binding = selected.SubjectElement.GetProperty("binding");
        var requested = binding.GetProperty("requested");
        var resolved = binding.GetProperty("resolved");
        var capabilities = resolved.GetProperty("capabilities");
        var endpoint = requested.GetProperty("endpoint");
        var reservation = selected.CaseElement.GetProperty("requestedReservation");
        var settings = resolved.GetProperty("settings").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        return new CaseExecutionRequest(runId, plan.PlanHash, selected.CaseId, selected.TrialId, selected.TrialSlot,
            selected.SubjectId, selected.ModelId, selected.ScenarioId,
            selected.CaseElement.GetProperty("observations").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToArray(),
            scenario.GetProperty("fixtureId").GetString()!, scenario.GetProperty("fixtureHash").GetString()!,
            scenario.GetProperty("assertionIds").EnumerateArray().Select(a => a.GetString()!).ToArray(),
            scenario.GetProperty("requiresNativeTools").GetBoolean(), true,
            scenario.GetProperty("maximumDispatches").GetInt32(),
            reservation.ValueKind == JsonValueKind.Object
                ? new TokenReservationEvidence(NullableLong(reservation, "inputTokens"), NullableLong(reservation, "outputTokens"),
                    NullableLong(reservation, "reasoningTokens"), NullableLong(reservation, "totalTokens"),
                    NullableString(reservation, "maximumCost"), NullableString(reservation, "currency"))
                : null,
            new SubjectBindingEvidence(requested.GetProperty("service").GetString()!,
                requested.GetProperty("accountScopeRef").GetString()!, requested.GetProperty("endpointProfile").GetString()!,
                requested.GetProperty("protocol").GetString()!, NullableString(requested, "apiVersion"),
                NullableString(requested, "revision"), requested.GetProperty("mutableAlias").GetBoolean(),
                requested.GetProperty("transportProfile").GetString()!, resolved.GetProperty("endpointId").GetString()!,
                resolved.GetProperty("provider").GetString()!, resolved.GetProperty("providerModel").GetString()!,
                resolved.GetProperty("baseUrl").GetString()!, NullableString(resolved, "profile"), settings,
                resolved.GetProperty("clientRequestTimeout").GetString()!, resolved.GetProperty("effectiveAttemptCeiling").GetString()!,
                capabilities.GetProperty("nativeToolCalling").GetBoolean(), requested.GetProperty("protocol").GetString()!,
                endpoint.GetProperty("apiKeySecretName").GetString()!),
            BuildBudget(plan),
            new DeadlinesEvidence(policy.GetProperty("attemptTimeout").GetString()!,
                policy.GetProperty("maximumDuration").GetString()!, policy.GetProperty("streamIdleTimeout").GetString()!),
            new LimitsEvidence(policy.GetProperty("maximumRequestBytes").GetInt64(),
                policy.GetProperty("maximumResponseBytes").GetInt64(),
                policy.GetProperty("maximumEventBytes").GetInt64()));
    }

    private static string[] ObservedContracts(JsonElement caseElement) =>
        caseElement.GetProperty("observations").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToArray();

    private static async Task<bool> CaptureCheckpointAsync(ExecutionEnvironment environment, string runDirectory)
    {
        try
        {
            var checkpoint = await LedgerCheckpoints.CaptureAsync(environment.Ledger.Ledger, environment.Time,
                CancellationToken.None).ConfigureAwait(false);
            File.WriteAllBytes(Path.Combine(runDirectory, "checkpoint.json"), LedgerCheckpoints.Export(checkpoint));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (int Passed, int Failed, int Blocked, int Cancelled, int Inconclusive, int NotRun, bool Complete)
        Summarize(IReadOnlyList<CaseOutcome> outcomes)
    {
        var summary = (Passed: 0, Failed: 0, Blocked: 0, Cancelled: 0, Inconclusive: 0, NotRun: 0);
        foreach (var outcome in outcomes)
        {
            switch (outcome.Outcome)
            {
                case AttemptOutcome.Passed: summary.Passed++; break;
                case AttemptOutcome.Failed: summary.Failed++; break;
                case AttemptOutcome.Blocked: summary.Blocked++; break;
                case AttemptOutcome.Cancelled: summary.Cancelled++; break;
                case AttemptOutcome.Inconclusive: summary.Inconclusive++; break;
                default: summary.NotRun++; break;
            }
        }
        return (summary.Passed, summary.Failed, summary.Blocked, summary.Cancelled, summary.Inconclusive, summary.NotRun,
            outcomes.Count > 0 && outcomes.All(o => o.Complete));
    }

    private static int ComputeExitCode(IReadOnlyList<CaseOutcome> outcomes, bool cancelled, bool interrupted, bool internalFailure)
    {
        if (internalFailure) return 5;
        if (cancelled) return 130;
        if (interrupted) return 4;
        if (outcomes.Count == 0) return 4;
        if (outcomes.Any(o => o.Outcome is AttemptOutcome.Blocked or AttemptOutcome.Cancelled
            or AttemptOutcome.Inconclusive or AttemptOutcome.NotRun)) return 4;
        if (outcomes.Any(o => o.Outcome == AttemptOutcome.Failed)) return 3;
        return 0;
    }

    private int Fail(string code, int exitCode)
    {
        _error.WriteLine(code);
        _output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
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

    private int FailEligibility(LiveEligibilityException exception)
    {
        _error.WriteLine("live-execution-not-eligible");
        _output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
        {
            kind = "run-refused",
            schemaVersion = "1.0",
            code = "live-execution-not-eligible",
            exitCode = 2,
            commandCompleted = false,
            qualificationPerformed = false,
            executionAuthorized = false,
            criticalReasons = exception.CriticalReasons,
            limitations = exception.Limitations
        })));
        return 2;
    }
}
