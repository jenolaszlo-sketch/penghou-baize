using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

#pragma warning disable xUnit1051

namespace Penghou.Baize.Qualification.Tests;

// Stage 4A transport-profile identity, physical-attempt bound, eligibility
// invalidation and budget/guard integration. No provider or secret read.
public sealed class Stage4aProfileTests
{
    [Fact]
    public void ReviewedProfileHasExactReproducibleIdentity()
    {
        var first = TransportProfiles.Describe("ReviewedNetwork", TransportProfiles.Http11ExactId);
        var second = TransportProfiles.Describe("ReviewedNetwork", TransportProfiles.Http11ExactId);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(TransportProfiles.StateQualified, first.State);
        Assert.Equal("1.1", first.ProtocolVersion);
        Assert.Equal("RequestVersionExact", first.ProtocolVersionPolicy);
        Assert.Equal(1, first.PhysicalAttemptUpperBound);
        Assert.Equal(1, first.HandlerSettings!.MaxConnectionsPerServer);
        Assert.False(first.HandlerSettings.UseProxy);
        Assert.False(first.HandlerSettings.AllowAutoRedirect);
        Assert.False(first.HandlerSettings.UseCookies);
        Assert.False(first.HandlerSettings.AutomaticAuthentication);
        Assert.False(first.HandlerSettings.RetryMiddleware);
        Assert.False(string.IsNullOrEmpty(first.HttpAssemblyHash));
        Assert.Equal(".NET 10.0.12", first.RuntimeFramework);
        Assert.Contains("2.0", first.UnsupportedProtocolVersions);
        Assert.Contains("3.0", first.UnsupportedProtocolVersions);

        var loopback = TransportProfiles.Describe("LoopbackDevelopment", TransportProfiles.Http11ExactId);
        Assert.Equal(TransportProfiles.StateQualified, loopback.State);
        Assert.NotEqual(first.Identity, loopback.Identity);

        var unreviewed = TransportProfiles.Describe("ReviewedNetwork", null);
        Assert.Equal(TransportProfiles.StateUnreviewed, unreviewed.State);
        Assert.Null(unreviewed.PhysicalAttemptUpperBound);
        Assert.NotEqual(first.Identity, unreviewed.Identity);
    }

    [Fact]
    public void PinnedTerminalMatchesReviewedProfile()
    {
        using var handler = (SocketsHttpHandler)PinnedTerminalHandlers.CreateRealNetworkTerminal();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(1, handler.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.ConnectTimeout);
        Assert.False(handler.EnableMultipleHttp2Connections);
    }

    [Fact]
    public async Task QualifiedProfilePlanRecordsBoundAndIdentity()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        var plan = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace.PlanDir, "plan.json"))).RootElement;
        var subject = plan.GetProperty("subjects")[0];
        var transport = subject.GetProperty("binding").GetProperty("resolved").GetProperty("transport");
        Assert.Equal(TransportProfiles.Http11ExactId, transport.GetProperty("profileId").GetString());
        Assert.Equal(TransportProfiles.StateQualified, transport.GetProperty("state").GetString());
        Assert.Equal(1, transport.GetProperty("physicalAttemptUpperBound").GetInt32());
        Assert.StartsWith("sha256:", transport.GetProperty("identity").GetString(), StringComparison.Ordinal);
        var planCase = plan.GetProperty("cases")[0];
        Assert.Equal(1, planCase.GetProperty("maximumPhysicalWireAttemptsPerSlot").GetInt32());
        Assert.Equal(3, plan.GetProperty("requirements").GetProperty("physicalWireAttempts").GetInt64());
        var blockers = planCase.GetProperty("liveEligibilityBlockers").EnumerateArray()
            .Select(blocker => blocker.GetString()!).ToArray();
        Assert.DoesNotContain("wire-attempt-bound-unproven", blockers);
        Assert.DoesNotContain("transport-profile-unreviewed", blockers);
    }

    [Fact]
    public async Task FullyBoundReviewedProfile_IsTransportEligible()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateEligible, assessment.State);
        Assert.Empty(assessment.CriticalReasons);
        Assert.Equal(1, assessment.PhysicalAttemptUpperBound);
        Assert.Equal(TransportProfiles.Http11ExactId, assessment.TransportProfileId);
        Assert.Equal("at-most-1-physical-http-request-attempt-per-admitted-invocation", assessment.WireAttemptBound);
        Assert.Contains("wire-and-preallocation-byte-bounds-unproven", assessment.Limitations);
    }

    [Fact]
    public async Task UnreviewedReviewedProfile_RemainsBlockedOnWireBound()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("deepseek-native-chat", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Contains("transport-profile-unreviewed", assessment.CriticalReasons);
        Assert.Contains("wire-attempt-bound-unproven", assessment.CriticalReasons);
        Assert.Null(assessment.PhysicalAttemptUpperBound);
        Assert.Equal("unknown-multiplicity", assessment.WireAttemptBound);
    }

    [Fact]
    public async Task LoopbackProfile_IsNeverLiveEligibleButHasABound()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("loopback-local-alpha", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Equal(["loopback-not-live-eligible"], assessment.CriticalReasons);
        Assert.Equal(1, assessment.PhysicalAttemptUpperBound);
        Assert.DoesNotContain("wire-attempt-bound-unproven", assessment.CriticalReasons);
    }

    [Fact]
    public async Task SubstitutedTransportIdentity_InvalidatesEligibility()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("openrouter-llama31-8b", "chat.baseline@3", inventory: Stage4Fixtures.Inventory)).AssertCompleted("plan-generated", 0);
        var source = File.ReadAllText(Path.Combine(workspace.PlanDir, "plan.json"));
        var node = JsonNode.Parse(source)!;
        node["subjects"]![0]!["binding"]!["resolved"]!["transport"]!["identity"] = "sha256:" + new string('0', 64);
        var bytes = CanonicalJson.Write(node);
        var directory = workspace.Out("substituted");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "plan.json"), bytes);
        File.WriteAllBytes(Path.Combine(directory, "identity.json"), CanonicalJson.Write(new
        {
            kind = "qualification-plan-identity",
            schemaVersion = "1.0",
            canonicalWriter = CanonicalJson.Version,
            planHash = CanonicalJson.Hash(bytes),
            executionAuthorized = false
        }));
        var plan = FrozenRunPlan.Load(Path.Combine(directory, "plan.json"));
        var assessment = LiveEligibility.Assess(plan, Select(plan), "run");
        Assert.Equal(LiveEligibility.StateBlocked, assessment.State);
        Assert.Contains("transport-profile-substituted", assessment.CriticalReasons);
        Assert.Contains("wire-attempt-bound-unproven", assessment.CriticalReasons);
    }

    [Fact]
    public async Task BudgetReservationScalesWithPhysicalAttemptBound()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var request = RunCommand.BuildRequest(plan, Select(plan), "run");
        var one = BudgetLedger.UnitsFor(request);
        Assert.Equal(1, one.HttpRequests);
        Assert.Equal(request.PlanCaseTokenReservation!.InputTokens, one.InputTokens);
        Assert.Equal(request.PlanCaseTokenReservation!.TotalTokens, one.TotalTokens);

        // Test-only arithmetic: the reviewed production profile is bound 1.
        var hypothetical = request with { MaximumPhysicalAttempts = 2 };
        var two = BudgetLedger.UnitsFor(hypothetical);
        Assert.Equal(2, two.HttpRequests);
        Assert.Equal(request.PlanCaseTokenReservation!.InputTokens * 2, two.InputTokens);
        Assert.Equal(request.PlanCaseTokenReservation!.TotalTokens * 2, two.TotalTokens);
    }

    [Fact]
    public async Task AdmissionGateDeniesBeyondPhysicalBoundEvenWithBudgetRoom()
    {
        using var workspace = new Stage3Workspace();
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var policy = CanonicalJson.Read<ExecutionPolicy>(Stage3Fixtures.Policy);
        var envelope = new BudgetEnvelopeEvidence(policy.MaximumModelCalls, policy.MaximumHttpRequests,
            policy.MaximumInputTokens, policy.MaximumOutputTokens, policy.MaximumReasoningTokens,
            policy.MaximumTotalTokens, policy.MaximumEstimatedCost, policy.Currency);
        await using var environment = await ExecutionEnvironment.CreateAsync(workspace.Out("env"), ExecutionFaults.None,
            inventory, envelope, TestContext.Current.CancellationToken);
        var dispatch = new HttpDispatchRecord("attempt", "dispatch", "run", "case", "trial", 1, "fake-alpha",
            "https://alpha.invalid/v1/chat/completions", "synthetic-terminal-transport", 0, 1);
        var first = await environment.Gate.AdmitAsync(dispatch, TestContext.Current.CancellationToken);
        Assert.True(first.Admitted);
        var second = await environment.Gate.AdmitAsync(dispatch with { DispatchId = "dispatch-2" },
            TestContext.Current.CancellationToken);
        Assert.False(second.Admitted);
        Assert.Equal("physical-attempt-bound-exceeded", second.DenialCode);
    }

    [Fact]
    public async Task GuardRejectsContentlessAndExpectContinueRequests()
    {
        var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
        var lease = new AttemptLease("attempt", "run", "case", "trial", 1, "fake-alpha", "POST",
            "https://alpha.invalid", "/v1", "SYNTHETIC_UNUSED_KEY_SLOT",
            CancellationToken.None, CancellationToken.None, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(5), 1 << 20, 4 << 20, 1);
        var gate = new Stage4Gate();
        using var scope = AttemptLeaseScope.Enter(lease);
        var guard = new QualificationAdmissionHandler(new RecordingTerminalHandler(), gate, "synthetic-terminal-transport");
        using var invoker = new HttpMessageInvoker(guard);

        var contentless = new HttpRequestMessage(HttpMethod.Post, "https://alpha.invalid/v1/chat/completions");
        var contentlessError = await Assert.ThrowsAsync<ExecutionException>(async () =>
            await invoker.SendAsync(contentless, TestContext.Current.CancellationToken));
        Assert.Equal("http-admission-requires-body", contentlessError.Code);

        using var expect = new HttpRequestMessage(HttpMethod.Post, "https://alpha.invalid/v1/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        expect.Headers.ExpectContinue = true;
        var expectError = await Assert.ThrowsAsync<ExecutionException>(async () =>
            await invoker.SendAsync(expect, TestContext.Current.CancellationToken));
        Assert.Equal("http-admission-expect-continue-rejected", expectError.Code);
        Assert.Equal(0, gate.Admissions);
    }

    [Fact]
    public async Task FaultingBodyDisposalStillReleasesConcurrency()
    {
        var gate = new Stage4Gate();
        var terminal = new FaultOnFirstDisposeTerminal();
        var lease = new AttemptLease("attempt", "run", "case", "trial", 1, "fake-alpha", "POST",
            "https://alpha.invalid", "/v1", "SYNTHETIC_UNUSED_KEY_SLOT",
            CancellationToken.None, CancellationToken.None, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(5), 1 << 20, 4 << 20, 1);
        using var scope = AttemptLeaseScope.Enter(lease);
        var guard = new QualificationAdmissionHandler(terminal, gate, "synthetic-terminal-transport");

        using var first = new HttpRequestMessage(HttpMethod.Post, "https://alpha.invalid/v1/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var response = await Send(guard, first);
        await Assert.ThrowsAnyAsync<Exception>(async () => response.Dispose());

        using var second = new HttpRequestMessage(HttpMethod.Post, "https://alpha.invalid/v1/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using var secondResponse = await Send(guard, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, gate.Admissions);
    }

    [Fact]
    public async Task ReportDistinguishesApplicationAndTransportObservations()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        var reportCase = Stage2Evidence.ReportDirectory(workspace.RunDir)
            .GetProperty("report").GetProperty("cases")[0];
        Assert.Equal(TransportProfiles.SyntheticProfileId, reportCase.GetProperty("transportProfileId").GetString());
        Assert.StartsWith("sha256:", reportCase.GetProperty("transportProfileIdentity").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, reportCase.GetProperty("physicalAttemptUpperBound").ValueKind);
        Assert.Equal("not-applicable-synthetic", reportCase.GetProperty("wireAttemptBound").GetString());
        Assert.Equal("SyntheticFake", reportCase.GetProperty("transportProfile").GetString());
    }

    [Fact]
    public async Task ReopenedBudgetLedgerRetainsReservedLiability()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var plan = FrozenRunPlan.Load(Path.Combine(workspace.PlanDir, "plan.json"));
        var request = RunCommand.BuildRequest(plan, Select(plan), "run");
        var units = BudgetLedger.UnitsFor(request);
        var directory = workspace.Out("ledger");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "evidence.db");
        var ledger = new EvidenceLedger(path, ExecutionFaults.None);
        var reservation = await BudgetLedger.ReserveAsync(ledger, request, "attempt", units,
            TestContext.Current.CancellationToken);
        Assert.Null(reservation.DenialCode);
        await ledger.DisposeAsync();

        var reopened = new EvidenceLedger(path, ExecutionFaults.None);
        var state = await BudgetLedger.ComputeAsync(reopened, request.Budget, TestContext.Current.CancellationToken);
        await reopened.DisposeAsync();
        Assert.Equal(1, state.ConsumedModelCalls);
        Assert.Equal(1, state.ConsumedHttpRequests);
        Assert.Equal(request.Budget.ModelCalls - 1, state.RemainingModelCalls);
        Assert.Equal(request.Budget.HttpRequests - 1, state.RemainingHttpRequests);
    }

    private static async Task<HttpResponseMessage> Send(HttpMessageHandler handler, HttpRequestMessage request)
    {
        using var invoker = new HttpMessageInvoker(handler);
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static RunCaseSelection Select(FrozenRunPlan plan)
    {
        var caseElement = plan.Cases[0];
        var subjectId = caseElement.GetProperty("subjectId").GetString()!;
        var subject = plan.Subjects.Single(s => s.GetProperty("id").GetString() == subjectId);
        var trial = caseElement.GetProperty("trials")[0];
        return new RunCaseSelection(caseElement.GetProperty("id").GetString()!, subjectId,
            caseElement.GetProperty("modelId").GetString()!, caseElement.GetProperty("scenario").GetString()!,
            caseElement, subject, trial.GetProperty("id").GetString()!, trial.GetProperty("slot").GetInt32());
    }

    private sealed class FaultOnFirstDisposeTerminal : HttpMessageHandler
    {
        private int _sends;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var send = Interlocked.Increment(ref _sends);
            HttpContent content = send == 1
                ? new StreamContent(new FaultingDisposeStream())
                : new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FaultingDisposeStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() => ValueTask.FromException(new IOException("dispose probe"));
        protected override void Dispose(bool disposing)
        {
            if (disposing) throw new IOException("dispose probe");
            base.Dispose(disposing);
        }
    }
}
