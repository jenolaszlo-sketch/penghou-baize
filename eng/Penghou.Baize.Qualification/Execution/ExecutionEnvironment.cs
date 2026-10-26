using Penghou.Baize.Qualification.Planning;
using Penghou.Zhinu;
using Penghou.Zhinu.Sqlite;

namespace Penghou.Baize.Qualification.Execution;

// Owns the durable components of one run directory: the Zhinu store/engine
// (workflow lifecycle), the Siming ledger (evidence) and the HTTP admission
// gate (budget authority at the last application boundary). Executors are
// pooled per subject so distinct subjects can never share configuration,
// counters or budget leases.
internal sealed class ExecutionEnvironment : IAsyncDisposable
{
    private readonly Inventory _inventory;
    private readonly BudgetEnvelopeEvidence _envelope;
    private readonly Dictionary<string, CaseExecutor> _executors = new(StringComparer.Ordinal);
    private readonly InvokerSlot _invoker;
    private readonly AdmissionGate _gate;

    public EvidenceLedger Ledger { get; }
    public WorkflowEngine Engine { get; }
    public ExecutionFaults Faults { get; }
    public TimeProvider Time { get; }

    private ExecutionEnvironment(EvidenceLedger ledger, WorkflowEngine engine, ExecutionFaults faults,
        TimeProvider time, Inventory inventory, BudgetEnvelopeEvidence envelope, InvokerSlot invoker,
        AdmissionGate gate)
    {
        Ledger = ledger;
        Engine = engine;
        Faults = faults;
        Time = time;
        _inventory = inventory;
        _envelope = envelope;
        _invoker = invoker;
        _gate = gate;
    }

    public static async Task<ExecutionEnvironment> CreateAsync(string runDirectory, ExecutionFaults faults,
        Inventory inventory, BudgetEnvelopeEvidence envelope, CancellationToken cancellationToken,
        Action? beforeHttpDispatch = null)
    {
        var time = TimeProvider.System;
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), faults);
        var store = new SqliteWorkflowStore(new ZhinuSqliteOptions
        {
            DatabasePath = Path.Combine(runDirectory, "workflow.db"),
            TimeProvider = time
        });
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var gate = new AdmissionGate(ledger, envelope, faults, beforeHttpDispatch);
        var invoker = new InvokerSlot();
        var workflow = new QualificationWorkflow(new WorkflowServices(ledger, invoker, faults, time));
        var registry = new WorkflowRegistry().Register<CaseExecutionRequest, TrialOutcomeRecord>(
            QualificationWorkflow.Name, QualificationWorkflow.Version, workflow);
        var engine = new WorkflowEngine(store, registry, new ZhinuOptions
        {
            MaxConcurrentWorkflows = 1,
            PollInterval = TimeSpan.FromMilliseconds(25),
            LeaseDuration = TimeSpan.FromSeconds(30),
            LeaseRenewalInterval = TimeSpan.FromSeconds(10)
        }, timeProvider: time);
        var environment = new ExecutionEnvironment(ledger, engine, faults, time, inventory, envelope, invoker, gate);
        invoker.Inner = new PooledInvoker(environment);
        workflow.Probe = (runId, token) => engine.GetRunAsync(runId, token);
        return environment;
    }

    public IHttpAdmissionGate Gate => _gate;

    public CaseExecutor ExecutorFor(string modelId, ModelEntry entry) =>
        _executors.TryGetValue(modelId, out var executor)
            ? executor
            : _executors[modelId] = CaseExecutor.Create(modelId, entry, _inventory, _gate, Faults);

    public async ValueTask DisposeAsync()
    {
        foreach (var executor in _executors.Values) executor.Dispose();
        _executors.Clear();
        await Engine.DisposeAsync().ConfigureAwait(false);
        await Ledger.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class InvokerSlot : ICaseInvoker
    {
        public ICaseInvoker? Inner { get; set; }
        public Task<QualificationResultV3> InvokeAsync(CaseExecutionRequest request, string attemptId,
            CancellationToken cancellationToken, CancellationToken deadlineToken) =>
            Inner is null ? throw new ExecutionException("invoker-not-wired")
                : Inner.InvokeAsync(request, attemptId, cancellationToken, deadlineToken);
    }

    private sealed class PooledInvoker(ExecutionEnvironment environment) : ICaseInvoker
    {
        public async Task<QualificationResultV3> InvokeAsync(CaseExecutionRequest request, string attemptId,
            CancellationToken cancellationToken, CancellationToken deadlineToken)
        {
            var entry = environment._inventory.Entries.Single(e => e.Id == request.ModelId);
            VerifyTransportBinding(entry, request);
            var executor = environment.ExecutorFor(request.ModelId, entry);
            var attemptTimeout = TimeSpan.Parse(request.Deadlines.AttemptTimeout,
                System.Globalization.CultureInfo.InvariantCulture);
            var lease = new AttemptLease(attemptId, request.RunId, request.CaseId, request.TrialId, request.TrialSlot,
                request.ModelId, "POST", executor.DestinationOrigin, executor.DestinationBasePath,
                request.Subject.SecretSlotReference, cancellationToken, deadlineToken,
                environment.Time.GetUtcNow() + attemptTimeout,
                TimeSpan.Parse(request.Deadlines.StreamIdleTimeout, System.Globalization.CultureInfo.InvariantCulture),
                request.Limits.MaximumRequestBytes, request.Limits.MaximumResponseBytes,
                request.MaximumPhysicalAttempts);
            using var scope = AttemptLeaseScope.Enter(lease);
            var context = new QualificationContextV3(
                (streamRequest, token) => executor.Stream(streamRequest, token),
                NativeToolCalling: request.Subject.NativeToolCalling,
                RequireRouterDiagnostics: request.RequiresRouterDiagnostics,
                OutputBudget: OutputBudgetSupport.Unknown,
                IdentityProtocol: IdentityProtocols.Parse(request.Subject.IdentityProtocol),
                Deadline: deadlineToken);
            return request.RequiresNativeTools
                ? await QualificationScenariosV3.RunSingleToolAsync(context, cancellationToken).ConfigureAwait(false)
                : await QualificationScenariosV3.RunBaselineAsync(context, cancellationToken).ConfigureAwait(false);
        }

        // The frozen subject, inventory entry and compiled profile must agree
        // before a lease, credential lookup or dispatch exists. Synthetic
        // execution is exempt from the live bound but not from identity checks.
        private static void VerifyTransportBinding(ModelEntry entry, CaseExecutionRequest request)
        {
            var entryRef = entry.TransportProfileRef ?? TransportProfiles.SyntheticProfileId;
            if (entry.TransportProfile != request.Subject.TransportProfile
                || entryRef != request.Subject.TransportProfileRef)
                throw new ExecutionException("transport-profile-substituted");
            var current = TransportProfiles.Describe(entry.TransportProfile, entry.TransportProfileRef);
            if (current.Identity != request.Subject.TransportProfileIdentity)
                throw new ExecutionException("transport-profile-substituted");
            if (entry.TransportProfile != "SyntheticFake")
            {
                if (!TransportProfiles.IsQualified(current)
                    || request.MaximumPhysicalAttempts != current.PhysicalAttemptUpperBound)
                    throw new ExecutionException("transport-profile-unqualified");
            }
            else if (request.MaximumPhysicalAttempts < 1)
            {
                throw new ExecutionException("physical-attempt-bound-invalid");
            }
        }
    }

    // The environment is the local accounting authority for HTTP dispatch
    // admission: remaining allowance comes from the Siming budget stream and
    // every admitted or denied dispatch is recorded as evidence.
    private sealed class AdmissionGate(EvidenceLedger ledger, BudgetEnvelopeEvidence envelope, ExecutionFaults faults,
        Action? beforeHttpDispatch)
        : IHttpAdmissionGate
    {
        private readonly Dictionary<string, int> _admittedByAttempt = new(StringComparer.Ordinal);
        private int _failedResultRecords;
        public int FailedResultRecords => Volatile.Read(ref _failedResultRecords);

        public async Task<HttpAdmissionDecision> AdmitAsync(HttpDispatchRecord dispatch, CancellationToken cancellationToken)
        {
            faults.BeforeHttpAdmission?.Invoke(dispatch.DispatchId);
            if (faults.DenyHttpAdmission?.Invoke(dispatch.DispatchId) == true)
            {
                await RecordDeniedAsync(dispatch, "admission-fault-denied", cancellationToken).ConfigureAwait(false);
                return new HttpAdmissionDecision(false, "admission-fault-denied", dispatch.DispatchId);
            }
            var state = await BudgetLedger.ComputeAsync(ledger, envelope, cancellationToken).ConfigureAwait(false);
            if (state.RemainingHttpDispatches <= 0 || state.RemainingHttpRequests <= 0)
            {
                await RecordDeniedAsync(dispatch, "budget-http-requests-exhausted", cancellationToken).ConfigureAwait(false);
                return new HttpAdmissionDecision(false, "budget-http-requests-exhausted", dispatch.DispatchId);
            }
            // An unexpected retry above the guard must not exceed the reviewed
            // physical-attempt bound, even when the global budget has room.
            int index;
            lock (_admittedByAttempt)
            {
                _admittedByAttempt.TryGetValue(dispatch.AttemptId, out var admitted);
                if (admitted >= dispatch.MaximumPhysicalAttempts)
                {
                    index = -1;
                }
                else
                {
                    index = admitted + 1;
                    _admittedByAttempt[dispatch.AttemptId] = index;
                }
            }
            if (index < 0)
            {
                await RecordDeniedAsync(dispatch, "physical-attempt-bound-exceeded", cancellationToken).ConfigureAwait(false);
                return new HttpAdmissionDecision(false, "physical-attempt-bound-exceeded", dispatch.DispatchId);
            }
            await ledger.AppendAsync(EvidenceLedger.BudgetStream, EvidenceLedger.HttpDispatchAdmitted,
                new HttpDispatchAdmittedEvidence("1.0", dispatch.AttemptId, dispatch.DispatchId, dispatch.RunId,
                    dispatch.CaseId, dispatch.TrialId, dispatch.TrialSlot, dispatch.ModelId,
                    dispatch.Destination, dispatch.BoundaryKind, index, dispatch.MaximumPhysicalAttempts),
                idempotencyKey: "http-admitted-" + dispatch.DispatchId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            faults.AfterHttpAdmission?.Invoke(dispatch.DispatchId);
            beforeHttpDispatch?.Invoke();
            return new HttpAdmissionDecision(true, null, dispatch.DispatchId);
        }

        public async Task RecordResultAsync(HttpDispatchRecord dispatch, int? statusCode, string phase, long bytesRead,
            bool completed, CancellationToken cancellationToken)
        {
            faults.BeforeHttpResultRecord?.Invoke(dispatch.DispatchId);
            try
            {
                await ledger.AppendAsync(EvidenceLedger.AttemptStream(dispatch.AttemptId), EvidenceLedger.HttpDispatchResult,
                    new HttpDispatchResultEvidence("1.0", dispatch.AttemptId, dispatch.DispatchId, dispatch.RunId,
                        dispatch.CaseId, dispatch.TrialId, dispatch.TrialSlot, dispatch.ModelId,
                        statusCode, phase, bytesRead, completed),
                    idempotencyKey: "http-result-" + dispatch.DispatchId + "-" + phase,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (EvidenceIoException)
            {
                Interlocked.Increment(ref _failedResultRecords);
                throw;
            }
            faults.AfterHttpResultRecord?.Invoke(dispatch.DispatchId);
        }

        private async Task RecordDeniedAsync(HttpDispatchRecord dispatch, string reason, CancellationToken cancellationToken)
        {
            await ledger.AppendAsync(EvidenceLedger.AttemptStream(dispatch.AttemptId), EvidenceLedger.HttpDispatchDenied,
                new HttpDispatchDeniedEvidence("1.0", dispatch.AttemptId, dispatch.DispatchId, dispatch.RunId,
                    dispatch.CaseId, dispatch.TrialId, dispatch.TrialSlot, dispatch.ModelId, reason),
                idempotencyKey: "http-denied-" + dispatch.DispatchId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}

internal static class IdentityProtocols
{
    public static ToolIdentityProtocol Parse(string protocol) => protocol switch
    {
        "openai-chat-completions" => ToolIdentityProtocol.OpenAiChatCompletions,
        "anthropic-messages" => ToolIdentityProtocol.AnthropicMessages,
        "ollama-chat" => ToolIdentityProtocol.OllamaChat,
        "gemini-generate-content" => ToolIdentityProtocol.GeminiGenerateContent,
        _ => ToolIdentityProtocol.Unspecified
    };
}
