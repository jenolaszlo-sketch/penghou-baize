using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

// Deterministic verification of the guarded transport composition: the real
// Baize adapter and neutral transport over the runner-owned admission guard
// with scripted terminal handlers. No provider call occurs anywhere.
public sealed class Stage3TransportTests
{
    private static async Task<ExecutionEnvironment> EnvironmentAsync(Stage3Workspace workspace,
        ExecutionFaults faults, string? inventoryJson = null, string? policyJson = null)
    {
        var inventory = CanonicalJson.Read<Inventory>(inventoryJson ?? Stage3Fixtures.Inventory);
        var policy = CanonicalJson.Read<ExecutionPolicy>(policyJson ?? Stage3Fixtures.Policy);
        var envelope = new BudgetEnvelopeEvidence(policy.MaximumModelCalls, policy.MaximumHttpRequests,
            policy.MaximumInputTokens, policy.MaximumOutputTokens, policy.MaximumReasoningTokens,
            policy.MaximumTotalTokens, policy.MaximumEstimatedCost, policy.Currency);
        return await ExecutionEnvironment.CreateAsync(workspace.Out("env"), faults, inventory, envelope,
            TestContext.Current.CancellationToken);
    }

    private static AttemptLease LeaseFor(string attemptId, CaseExecutor executor, string modelId,
        string secretSlot, string runId = "run", string caseId = "case", string trialId = "trial")
    {
        return new AttemptLease(attemptId, runId, caseId, trialId, 1, modelId, "POST",
            executor.DestinationOrigin, executor.DestinationBasePath, secretSlot,
            CancellationToken.None, CancellationToken.None, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(30), 1 << 20, 4 << 20, 1);
    }

    private static async Task<QualificationResultV3> RunScenarioAsync(CaseExecutor executor, string attemptId,
        bool tools, string secretSlot = "SYNTHETIC_UNUSED_KEY_SLOT")
    {
        using var scope = AttemptLeaseScope.Enter(LeaseFor(attemptId, executor, "fake-alpha", secretSlot));
        var context = new QualificationContextV3(
            (request, token) => executor.Stream(request, token),
            NativeToolCalling: true,
            RequireRouterDiagnostics: true,
            OutputBudget: OutputBudgetSupport.Unknown,
            IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
        return tools
            ? await QualificationScenariosV3.RunSingleToolAsync(context, TestContext.Current.CancellationToken)
            : await QualificationScenariosV3.RunBaselineAsync(context, TestContext.Current.CancellationToken);
    }

    // 1. An eligible synthetic configuration reaches the actual Baize adapter
    // and the controlled transport: every request crosses the admission guard
    // and both dispatch phases are recorded.
    [Fact]
    public async Task SyntheticCase_ReachesAdapterAndGuardedTransport()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAllDirectory(workspace.RunDir);
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchAdmitted));
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchResult));
        var admitted = Stage2Evidence.Event(entries, EvidenceLedger.HttpDispatchAdmitted);
        var finished = Stage2Evidence.Event(entries, EvidenceLedger.AttemptFinished);
        Assert.Equal(finished.GetProperty("attemptId").GetString(), admitted.GetProperty("attemptId").GetString());
        Assert.Equal(finished.GetProperty("caseId").GetString(), admitted.GetProperty("caseId").GetString());
        Assert.Equal(finished.GetProperty("trialId").GetString(), admitted.GetProperty("trialId").GetString());
        Assert.Equal("synthetic-terminal-transport", admitted.GetProperty("boundaryKind").GetString());
        Assert.Equal("https://alpha.invalid/v1/chat/completions", admitted.GetProperty("destination").GetString());
        var results = Stage2Evidence.Events(entries, EvidenceLedger.HttpDispatchResult);
        Assert.Contains(results, result => result.GetProperty("phase").GetString() == "headers-received"
            && result.GetProperty("statusCode").GetInt32() == 200);
        Assert.Contains(results, result => result.GetProperty("phase").GetString() == "complete"
            && result.GetProperty("completed").GetBoolean() && result.GetProperty("bytesRead").GetInt64() > 0);
        var eligibility = Stage2Evidence.Event(entries, EvidenceLedger.EligibilityAssessed);
        Assert.Equal("SyntheticOnly", eligibility.GetProperty("state").GetString());
    }

    // 11. Siming evidence preserves exact subject and attempt identities.
    [Fact]
    public async Task DispatchEvidence_MatchesReservationAndAttemptIdentity()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAllDirectory(workspace.RunDir);
        var reservation = Stage2Evidence.Event(entries, EvidenceLedger.AttemptReserved);
        var intent = Stage2Evidence.Event(entries, EvidenceLedger.DispatchIntent);
        var admitted = Stage2Evidence.Event(entries, EvidenceLedger.HttpDispatchAdmitted);
        var finished = Stage2Evidence.Event(entries, EvidenceLedger.AttemptFinished);
        var attemptId = finished.GetProperty("attemptId").GetString();
        Assert.Equal(attemptId, reservation.GetProperty("attemptId").GetString());
        Assert.Equal(attemptId, intent.GetProperty("attemptId").GetString());
        Assert.Equal(attemptId, admitted.GetProperty("attemptId").GetString());
        foreach (var record in new[] { reservation, intent, admitted, finished })
        {
            Assert.Equal("fake-alpha", record.GetProperty("modelId").GetString());
            Assert.Equal(finished.GetProperty("caseId").GetString(), record.GetProperty("caseId").GetString());
            Assert.Equal(finished.GetProperty("trialId").GetString(), record.GetProperty("trialId").GetString());
        }
    }

    // 13. Provider errors remain distinct from contract failures.
    [Fact]
    public async Task ProviderFailure_IsInfrastructureNotContractFailure()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-unavailable", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-unavailable", "chat.baseline@3")).AssertCompleted("run-summary", 4);
        var reportCase = Stage2Evidence.ReportDirectory(workspace.RunDir)
            .GetProperty("report").GetProperty("cases")[0];
        Assert.Equal("Blocked", reportCase.GetProperty("outcome").GetString());
        Assert.Equal("infrastructure-failure", reportCase.GetProperty("suggestedAttributionClass").GetString());
        Assert.Equal("transport-response", reportCase.GetProperty("failureStage").GetString());
        var report = File.ReadAllText(Path.Combine(workspace.RunDir, "reports", "report.md"));
        Assert.Contains("infrastructure-failure", report);
    }

    // 3. Request-count exhaustion prevents additional sends at the guard.
    [Fact]
    public async Task HttpBudgetExhaustion_DeniesFurtherDispatches()
    {
        using var workspace = new Stage3Workspace();
        var tight = Stage3Fixtures.Policy.Replace("\"maximumHttpRequests\": 12", "\"maximumHttpRequests\": 1",
            StringComparison.Ordinal);
        await using var environment = await EnvironmentAsync(workspace, ExecutionFaults.None, policyJson: tight);
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var entry = inventory.Entries.Single(e => e.Id == "fake-alpha");
        using var executor = environment.ExecutorFor("fake-alpha", entry);
        using (var scope = AttemptLeaseScope.Enter(LeaseFor("attempt-a", executor, "fake-alpha", "SYNTHETIC_UNUSED_KEY_SLOT")))
        {
            var first = await environment.Gate.AdmitAsync(FirstDispatch("attempt-a"), TestContext.Current.CancellationToken);
            Assert.True(first.Admitted);
            var second = await environment.Gate.AdmitAsync(SecondDispatch("attempt-a"), TestContext.Current.CancellationToken);
            Assert.False(second.Admitted);
            Assert.Equal("budget-http-requests-exhausted", second.DenialCode);
        }
        var entries = Stage2Evidence.ReadAllDirectory(workspace.Out("env"));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchAdmitted));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchDenied));
        var denied = Stage2Evidence.Event(entries, EvidenceLedger.HttpDispatchDenied);
        Assert.Equal("budget-http-requests-exhausted", denied.GetProperty("reasonCode").GetString());
    }

    private static HttpDispatchRecord FirstDispatch(string attemptId) => new(attemptId, "dispatch-a", "run",
        "case", "trial", 1, "fake-alpha", "https://alpha.invalid/v1/chat/completions", "synthetic-terminal-transport", 0);
    private static HttpDispatchRecord SecondDispatch(string attemptId) => new(attemptId, "dispatch-b", "run",
        "case", "trial", 1, "fake-alpha", "https://alpha.invalid/v1/chat/completions", "synthetic-terminal-transport", 0);

    // 4. Concurrent requests cannot exceed the configured concurrency of one.
    [Fact]
    public async Task ConcurrentSends_AreSerializedToOne()
    {
        using var workspace = new Stage3Workspace();
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var entry = inventory.Entries.Single(e => e.Id == "fake-alpha");
        var gate = new CountingGate();
        var terminal = new HoldingTerminal(TimeSpan.FromMilliseconds(250));
        using var executor = CaseExecutor.CreateWithTerminal("fake-alpha", entry, inventory, gate, ExecutionFaults.None, terminal);
        var tasks = Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            var result = await RunScenarioAsync(executor, "attempt-" + index, tools: false);
            return result;
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.True(result.Succeeded, JsonSerializer.Serialize(result)));
        Assert.Equal(4, terminal.Sends);
        Assert.Equal(1, terminal.MaxObservedConcurrency);
        Assert.Equal(4, gate.Admissions);
    }

    // 5. A hidden retry handler above the guard is counted; the second
    // dispatch is denied at the cap.
    [Fact]
    public async Task RetryingWrapper_SecondDispatchDeniedAtCap()
    {
        using var workspace = new Stage3Workspace();
        var tight = Stage3Fixtures.Policy.Replace("\"maximumHttpRequests\": 12", "\"maximumHttpRequests\": 1",
            StringComparison.Ordinal);
        await using var environment = await EnvironmentAsync(workspace, ExecutionFaults.None, policyJson: tight);
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var entry = inventory.Entries.Single(e => e.Id == "fake-alpha");
        using var executor = CaseExecutor.CreateWithChainWrapper("fake-alpha", entry, inventory, environment.Gate,
            ExecutionFaults.None, new FailOnceTerminal(), guard => new RetryAboveGuard(guard));
        using var scope = AttemptLeaseScope.Enter(LeaseFor("attempt-r", executor, "fake-alpha", "SYNTHETIC_UNUSED_KEY_SLOT"));
        var context = new QualificationContextV3(
            (request, token) => executor.Stream(request, token),
            NativeToolCalling: true, RequireRouterDiagnostics: true,
            IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
        var result = await QualificationScenariosV3.RunBaselineAsync(context, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        var entries = Stage2Evidence.ReadAllDirectory(workspace.Out("env"));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchAdmitted));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchDenied));
    }

    // 6. Response size violations stop execution before delivery completes.
    [Fact]
    public async Task OversizedResponse_StopsAtConfiguredCeiling()
    {
        using var workspace = new Stage3Workspace();
        var tightBytes = Stage3Fixtures.Policy.Replace("\"maximumResponseBytes\": 4194304",
            "\"maximumResponseBytes\": 4096", StringComparison.Ordinal);
        var inventory = Stage3Fixtures.Inventory;
        await using var environment = await EnvironmentAsync(workspace, ExecutionFaults.None, inventoryJson: inventory,
            policyJson: tightBytes);
        var parsed = CanonicalJson.Read<Inventory>(inventory);
        var entry = parsed.Entries.Single(e => e.Id == "fake-large-body");
        using var executor = environment.ExecutorFor("fake-large-body", entry);
        using var scope = AttemptLeaseScope.Enter(LeaseFor("attempt-l", executor, "fake-large-body", "SYNTHETIC_UNUSED_KEY_SLOT"));
        var context = new QualificationContextV3(
            (request, token) => executor.Stream(request, token),
            NativeToolCalling: true, RequireRouterDiagnostics: true,
            IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
        var result = await QualificationScenariosV3.RunBaselineAsync(context, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal(ContractDisposition.Blocked, result.Contracts[0].Disposition);
    }

    // 7. Streaming idle deadline: a stalled body read fails at the idle bound.
    [Fact]
    public async Task StalledStream_IdleDeadlineEnforced()
    {
        using var workspace = new Stage3Workspace();
        var shortIdle = Stage3Fixtures.Policy.Replace("\"streamIdleTimeout\": \"00:00:30\"",
            "\"streamIdleTimeout\": \"00:00:03\"", StringComparison.Ordinal);
        (await workspace.PlanAsync("fake-slow-chunk", "chat.baseline@3", policy: shortIdle))
            .AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-slow-chunk", "chat.baseline@3")).AssertCompleted("run-summary", 4);
        var finished = Stage2Evidence.Event(Stage2Evidence.ReadAllDirectory(workspace.RunDir),
            EvidenceLedger.AttemptFinished);
        Assert.Equal("Blocked", finished.GetProperty("outcome").GetString());
        Assert.Equal("timeout", finished.GetProperty("failureCategory").GetString());
        Assert.Equal("transport-lifecycle", finished.GetProperty("failureStage").GetString());
        Assert.Equal("infrastructure-failure", finished.GetProperty("suggestedAttributionClass").GetString());
    }

    // 10. Destination substitution is rejected at the guard.
    [Fact]
    public async Task DestinationSubstitution_Rejected()
    {
        using var workspace = new Stage3Workspace();
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var entry = inventory.Entries.Single(e => e.Id == "fake-alpha");
        var gate = new CountingGate();
        using var executor = CaseExecutor.Create("fake-alpha", entry, inventory, gate, ExecutionFaults.None);
        var lease = LeaseFor("attempt-d", executor, "fake-alpha", "SYNTHETIC_UNUSED_KEY_SLOT");
        using var scope = AttemptLeaseScope.Enter(lease);
        var guard = new QualificationAdmissionHandler(new RecordingTerminalHandler(), gate, "synthetic-terminal-transport");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://substituted.invalid/v1/chat/completions");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var exception = await Assert.ThrowsAsync<ExecutionException>(async () =>
            await new HttpMessageInvoker(guard).SendAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal("http-destination-substitution", exception.Code);
        Assert.Equal(0, gate.Admissions);
    }

    // 8. Without an attempt lease the guard denies admission entirely.
    [Fact]
    public async Task AdmissionWithoutLease_Denied()
    {
        using var workspace = new Stage3Workspace();
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var entry = inventory.Entries.Single(e => e.Id == "fake-alpha");
        var gate = new CountingGate();
        using var executor = CaseExecutor.Create("fake-alpha", entry, inventory, gate, ExecutionFaults.None);
        var guard = new QualificationAdmissionHandler(new RecordingTerminalHandler(), gate, "synthetic-terminal-transport");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://alpha.invalid/v1/chat/completions");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var exception = await Assert.ThrowsAsync<ExecutionException>(async () =>
            await new HttpMessageInvoker(guard).SendAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal("http-admission-without-attempt-lease", exception.Code);
        Assert.Equal(0, gate.Admissions);
    }

    // 12. Zhinu recovery never manufactures successful qualification evidence:
    // the interrupted attempt keeps its identity and stays interrupted.
    [Fact]
    public async Task Recovery_DoesNotFabricateSuccessEvidence()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        using var interrupt = new CancellationTokenSource();
        var faults = new ExecutionFaults { Interrupt = interrupt, AfterIntentAppend = _ => interrupt.Cancel() };
        await Assert.ThrowsAsync<SimulatedProcessCrashException>(async () =>
            await workspace.RunAsync("fake-alpha", "chat.baseline@3", faults: faults));
        (await workspace.ResumeAsync(faults: new ExecutionFaults())).AssertCompleted("run-summary", 0);
        var entries = Stage2Evidence.ReadAllDirectory(workspace.RunDir);
        Assert.Equal(2, Stage2Evidence.Count(entries, EvidenceLedger.DispatchIntent));
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.TrialFinished));
        var trial = Stage2Evidence.Event(entries, EvidenceLedger.TrialFinished);
        Assert.Single(trial.GetProperty("attemptIds").EnumerateArray());
        Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.AttemptInterrupted));
    }

    private sealed class CountingGate : IHttpAdmissionGate
    {
        private int _admissions;
        public int Admissions => Volatile.Read(ref _admissions);
        public int FailedResultRecords => 0;
        public Task<HttpAdmissionDecision> AdmitAsync(HttpDispatchRecord dispatch, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _admissions);
            return Task.FromResult(new HttpAdmissionDecision(true, null, dispatch.DispatchId));
        }
        public Task RecordResultAsync(HttpDispatchRecord dispatch, int? statusCode, string phase, long bytesRead,
            bool completed, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HoldingTerminal(TimeSpan hold) : HttpMessageHandler
    {
        private int _active;
        private int _maxObserved;
        private int _sends;
        public int Sends => Volatile.Read(ref _sends);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObserved);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sends);
            var active = Interlocked.Increment(ref _active);
            var max = Volatile.Read(ref _maxObserved);
            while (active > max && Interlocked.CompareExchange(ref _maxObserved, active, max) != max) { max = Volatile.Read(ref _maxObserved); }
            await Task.Delay(hold, cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
            const string body = "data: {\"choices\":[{\"delta\":{\"content\":\"\\nBAIZE_\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"OK\\r\\n\"},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3,\"total_tokens\":15}}\n\n" +
                "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class RetryAboveGuard(HttpMessageHandler inner) : HttpMessageHandler
    {
        private int _attempts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt == 1)
            {
                var first = await new HttpMessageInvoker(inner).SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)first.StatusCode < 500)
                    return first;
                first.Dispose();
            }
            return await new HttpMessageInvoker(inner).SendAsync(Clone(request), cancellationToken).ConfigureAwait(false);
        }
        private static HttpRequestMessage Clone(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            clone.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return clone;
        }
    }

    private sealed class FailOnceTerminal : HttpMessageHandler
    {
        private int _sends;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _sends) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            const string body = "data: {\"choices\":[{\"delta\":{\"content\":\"\\nBAIZE_\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"OK\\r\\n\"},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3,\"total_tokens\":15}}\n\n" +
                "data: [DONE]\n\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
        }
    }
}

// Adapts a run directory to the Stage 2 evidence reader without a workspace.
internal sealed class Stage2WorkspaceShim(string runDirectory) : IDisposable
{
    public string RunDir => runDirectory;
    public void Dispose() { }
}
