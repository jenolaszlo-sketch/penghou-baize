using System.Net;
using System.Net.Http;
using System.Text;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

#pragma warning disable xUnit1051

namespace Penghou.Baize.Qualification.Tests;

// Deterministic loopback probes over the real Baize OpenAI adapter, neutral
// transport, admission handler and SocketsHttpHandler. The controlled server
// is local; no provider or credential is involved.
public sealed class Stage4aLoopbackTests
{
    [Fact]
    public async Task NormalRequest_UsesOneConnectionOneServerRequest()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.Baseline);
        await harness.RunSemanticAsync("attempt-normal");
        Assert.Equal(1, harness.Gate.Admissions);
        Assert.Equal(1, harness.Server.ConnectionCount);
        var request = Assert.Single(harness.Server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/chat/completions", request.Target);
        Assert.True(request.BodyComplete);
        Assert.Contains(harness.Gate.Results, result => result.Phase == "complete" && result.Completed);
    }

    [Fact]
    public async Task SequentialRequests_ReuseOnePooledConnection()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.Baseline);
        await harness.RunSemanticAsync("attempt-one");
        await harness.RunSemanticAsync("attempt-two");
        Assert.Equal(2, harness.Gate.Admissions);
        Assert.Equal(2, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
    }

    [Fact]
    public async Task StreamingResponse_HeadersArriveBeforeBodyCompletes()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.StreamingBaseline);
        var run = harness.RunSemanticAsync("attempt-stream");
        await harness.Gate.HeadersReceived.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted);
        harness.Server.ReleaseBody();
        await run;
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
    }

    [Fact]
    public async Task ConnectionRefused_AdmitsButNoRequestReachesServer()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.Baseline);
        harness.Server.StopListening();
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-refused",
            attemptTimeout: TimeSpan.FromSeconds(2)));
        Assert.Equal(1, harness.Gate.Admissions);
        Assert.Equal(0, harness.Server.ConnectionCount);
        Assert.Empty(harness.Server.Requests);
    }

    [Fact]
    public async Task CloseBeforeRequest_NoServerRequestAndNoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.CloseImmediately);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-close-before"));
        await harness.WaitForConnectionAsync();
        Assert.Equal(1, harness.Gate.Admissions);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Empty(harness.Server.Requests);
    }

    [Fact]
    public async Task CloseAfterHeaders_RecordsPartialAndNoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.CloseAfterHeaders);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-close-headers"));
        await harness.WaitForConnectionAsync();
        var request = Assert.Single(harness.Server.Requests);
        Assert.True(request.HeadersComplete);
        Assert.False(request.BodyComplete);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task CloseAfterBody_NoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.CloseAfterBody);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-close-body"));
        var request = Assert.Single(harness.Server.Requests);
        Assert.True(request.BodyComplete);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task TimeoutAwaitingHeaders_NoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.HoldAfterBody);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-timeout",
            attemptTimeout: TimeSpan.FromMilliseconds(400)));
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task PartialResponseThenDisconnect_NoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.PartialResponseThenClose);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-partial"));
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task IdleDeadlineWhileBodyStalls_NoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.StallAfterFirstChunk);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-idle",
            attemptTimeout: TimeSpan.FromSeconds(5), idleTimeout: TimeSpan.FromMilliseconds(300)));
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task ReadCancellation_NoRetry()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.StallAfterFirstChunk);
        using var cancellation = new CancellationTokenSource();
        var run = harness.RunSemanticAsync("attempt-cancel", attemptTimeout: TimeSpan.FromSeconds(10),
            cancellationToken: cancellation.Token);
        await harness.Server.FirstChunkSent.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        await using var target = new ScriptedLoopbackServer(LoopbackMode.Baseline);
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.Redirect,
            $"http://127.0.0.1:{target.Port}/v1/chat/completions");
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-redirect"));
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(0, target.ConnectionCount);
        Assert.Empty(target.Requests);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task StaleKeepAliveConnection_RecoversWithoutReplay()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.KeepAliveThenCloseOnceThenBaseline);
        await harness.RunSemanticAsync("attempt-first");
        await harness.RunSemanticAsync("attempt-second");
        Assert.Equal(2, harness.Gate.Admissions);
        Assert.Equal(2, harness.Server.CompleteRequests);
        Assert.Equal(2, harness.Server.ConnectionCount);
    }

    [Fact]
    public async Task AuthenticationChallenge_IsNotRetried()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.Unauthorized);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-auth"));
        Assert.Equal(1, harness.Server.CompleteRequests);
        Assert.Equal(1, harness.Server.ConnectionCount);
        Assert.Equal(1, harness.Gate.Admissions);
    }

    [Fact]
    public async Task ConnectionPoolRecoversAfterFailedRequest()
    {
        await using var harness = Stage4LoopbackHarness.Create(LoopbackMode.CloseAfterBodyOnceThenBaseline);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunSemanticAsync("attempt-fail"));
        await harness.RunSemanticAsync("attempt-recover");
        Assert.Equal(2, harness.Gate.Admissions);
        Assert.Equal(2, harness.Server.CompleteRequests);
        Assert.Equal(2, harness.Server.ConnectionCount);
    }

    [Fact]
    public async Task ApplicationRetryAboveGuard_IsCountedAndBoundedByRealGate()
    {
        using var workspace = new Stage3Workspace();
        await using var server = new ScriptedLoopbackServer(LoopbackMode.ServiceUnavailableThenBaseline);
        var slot = "LOOPBACK_TEST_SLOT_" + Guid.NewGuid().ToString("N");
        var inventoryJson = Stage4Fixtures.Inventory
            .Replace("http://127.0.0.1:51234/v1", $"http://127.0.0.1:{server.Port}/v1", StringComparison.Ordinal)
            .Replace("LOOPBACK_TEST_SLOT", slot, StringComparison.Ordinal);
        var inventory = CanonicalJson.Read<Inventory>(inventoryJson);
        var policy = CanonicalJson.Read<ExecutionPolicy>(Stage4Fixtures.Policy);
        var previous = Environment.GetEnvironmentVariable(slot);
        Environment.SetEnvironmentVariable(slot, "synthetic-not-a-credential");
        try
        {
            var envelope = new BudgetEnvelopeEvidence(policy.MaximumModelCalls, policy.MaximumHttpRequests,
                policy.MaximumInputTokens, policy.MaximumOutputTokens, policy.MaximumReasoningTokens,
                policy.MaximumTotalTokens, policy.MaximumEstimatedCost, policy.Currency);
            await using var environment = await ExecutionEnvironment.CreateAsync(workspace.Out("env"),
                ExecutionFaults.None, inventory, envelope, TestContext.Current.CancellationToken);
            var entry = inventory.Entries.Single(e => e.Id == "loopback-local-alpha");
            using var executor = CaseExecutor.CreateWithChainWrapper("loopback-local-alpha", entry, inventory,
                environment.Gate, ExecutionFaults.None, PinnedTerminalHandlers.CreateRealNetworkTerminal(),
                guard => new RetryAboveGuard(guard));
            var timeout = TimeSpan.FromSeconds(10);
            using var deadline = new CancellationTokenSource(timeout);
            var lease = new AttemptLease("attempt-retry", "run", "case", "trial", 1, "loopback-local-alpha",
                "POST", executor.DestinationOrigin, executor.DestinationBasePath, slot,
                TestContext.Current.CancellationToken, deadline.Token, DateTimeOffset.UtcNow + timeout,
                TimeSpan.FromSeconds(5), 1 << 20, 4 << 20, 1);
            using var scope = AttemptLeaseScope.Enter(lease);
            var request = QualificationFixtures.CreateBaselineRequest();
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await foreach (var _ in executor.Stream(request, TestContext.Current.CancellationToken)
                    .WithCancellation(TestContext.Current.CancellationToken))
                {
                }
            });
            Assert.Equal(1, server.CompleteRequests);
            var entries = Stage2Evidence.ReadAllDirectory(workspace.Out("env"));
            Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchAdmitted));
            Assert.Equal(1, Stage2Evidence.Count(entries, EvidenceLedger.HttpDispatchDenied));
            var denied = Stage2Evidence.Event(entries, EvidenceLedger.HttpDispatchDenied);
            Assert.Equal("physical-attempt-bound-exceeded", denied.GetProperty("reasonCode").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(slot, previous);
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
                if ((int)first.StatusCode < 500) return first;
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
}
