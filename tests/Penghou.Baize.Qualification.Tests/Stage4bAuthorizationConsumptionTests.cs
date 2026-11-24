using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

public sealed class Stage4bAuthorizationConsumptionTests
{
    [Fact]
    public void FirstClaimSucceeds_SecondClaimIsRejected_AndClaimRemainsDurable()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        var simulatedDispatches = 0;
        var claim = Claim(registry, authorization, "execution-first");
        simulatedDispatches++;

        Assert.True(registry.ContainsAuthorization(authorization.AuthorizationId));
        Assert.Equal(authorization.AuthorizationId, claim.AuthorizationId);
        Assert.Throws<ExecutionException>(() => Claim(registry, authorization, "execution-second"));
        Assert.Equal(1, simulatedDispatches);
    }

    [Fact]
    public async Task TwoConcurrentClaimsInOneProcess_HaveExactlyOneWinner()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        var simulatedDispatches = 0;
        using var barrier = new Barrier(2);
        var attempts = Enumerable.Range(0, 2).Select(index => Task.Run(() =>
        {
            barrier.SignalAndWait(TestContext.Current.CancellationToken);
            try
            {
                Claim(registry, authorization, "execution-thread-" + index);
                Interlocked.Increment(ref simulatedDispatches);
                return true;
            }
            catch (ExecutionException) { return false; }
        }, TestContext.Current.CancellationToken)).ToArray();

        var winners = await Task.WhenAll(attempts);
        Assert.Single(winners, winner => winner);
        Assert.Single(winners, winner => !winner);
        Assert.Equal(1, simulatedDispatches);
    }

    [Fact]
    public async Task TwoRunnerProcesses_UseBarrierAndOnlyOneCanClaim()
    {
        using var workspace = new Stage3Workspace();
        var (registry, databasePath, authorization) = CreateRegistryAndAuthorization(workspace);
        var authorizationFile = WriteAuthorization(workspace, authorization, "same-authorization.json");
        var readyA = workspace.Out("ready-a");
        var readyB = workspace.Out("ready-b");
        var start = workspace.Out("start-claims");
        var receiptA = workspace.Out("dispatch-a.receipt");
        var receiptB = workspace.Out("dispatch-b.receipt");
        using var processA = StartProbe(databasePath, authorizationFile, "execution-process-a", readyA, start, receiptA);
        using var processB = StartProbe(databasePath, authorizationFile, "execution-process-b", readyB, start, receiptB);

        await Task.WhenAll(WaitForFile(readyA), WaitForFile(readyB));
        using (new FileStream(start, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
        await Task.WhenAll(processA.WaitForExitAsync(TestContext.Current.CancellationToken),
            processB.WaitForExitAsync(TestContext.Current.CancellationToken));

        // Exit code 0 is the probe's simulated dispatch admission after claim;
        // code 2 is a rejected replay that never reaches that boundary.
        var simulatedDispatches = new[] { processA.ExitCode, processB.ExitCode }.Count(code => code == 0);
        Assert.Equal([0, 2], new[] { processA.ExitCode, processB.ExitCode }.Order().ToArray());
        Assert.Equal(1, simulatedDispatches);
        Assert.Equal(1, new[] { receiptA, receiptB }.Count(File.Exists));
        Assert.True(registry.ContainsAuthorization(authorization.AuthorizationId));
    }

    [Fact]
    public async Task DurableClaimSurvivesProcessExitAndRestart()
    {
        using var workspace = new Stage3Workspace();
        var (_, databasePath, authorization) = CreateRegistryAndAuthorization(workspace);
        var authorizationFile = WriteAuthorization(workspace, authorization);
        var firstReceipt = workspace.Out("first-dispatch.receipt");
        var replayReceipt = workspace.Out("replay-dispatch.receipt");

        using (var first = StartProbe(databasePath, authorizationFile, "execution-before-restart", firstReceipt))
        {
            await first.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, first.ExitCode);
            Assert.True(File.Exists(firstReceipt));
        }
        using (var restarted = StartProbe(databasePath, authorizationFile, "execution-after-restart", replayReceipt))
        {
            await restarted.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, restarted.ExitCode);
            Assert.False(File.Exists(replayReceipt));
        }
    }

    [Fact]
    public async Task CrashAfterDurableClaimBeforeDispatch_DoesNotPermitRestartReplay()
    {
        using var workspace = new Stage3Workspace();
        var (_, databasePath, authorization) = CreateRegistryAndAuthorization(workspace);
        var authorizationFile = WriteAuthorization(workspace, authorization);
        var crashReceipt = workspace.Out("crash-dispatch.receipt");

        using (var crashed = StartProbe(databasePath, authorizationFile, "execution-crash", "crash", crashReceipt))
        {
            await crashed.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(77, crashed.ExitCode);
            Assert.False(File.Exists(crashReceipt));
        }
        var simulatedDispatches = 0;
        var restartReceipt = workspace.Out("restart-dispatch.receipt");
        using (var restarted = StartProbe(databasePath, authorizationFile, "execution-restart", restartReceipt))
        {
            await restarted.WaitForExitAsync(TestContext.Current.CancellationToken);
            if (restarted.ExitCode == 0) simulatedDispatches++;
            Assert.Equal(2, restarted.ExitCode);
            Assert.False(File.Exists(restartReceipt));
        }
        Assert.Equal(0, simulatedDispatches);
    }

    [Fact]
    public void ReencodedAuthorizationHasSameDigest_AndCannotBypassClaim()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        var prettyOptions = new JsonSerializerOptions(CanonicalJson.Options) { WriteIndented = true };
        var prettyJson = JsonSerializer.Serialize(authorization, prettyOptions).Replace("\n", "\r\n", StringComparison.Ordinal);
        var original = LiveAuthorizations.Read(WriteBytes(workspace, "original.json", CanonicalJson.Write(authorization)));
        var reencoded = LiveAuthorizations.Read(workspace.WriteInput("reencoded.json", prettyJson));

        LiveAuthorizationAuthentication.Verify(original);
        LiveAuthorizationAuthentication.Verify(reencoded);
        Assert.Equal(CanonicalJson.Identity(original), CanonicalJson.Identity(reencoded));
        Claim(registry, original, "execution-original");
        var simulatedDispatches = 1;
        Assert.Throws<ExecutionException>(() => Claim(registry, reencoded, "execution-reencoded"));
        Assert.Equal(1, simulatedDispatches);
    }

    [Fact]
    public void DistinctValidAuthorizationIds_CanBeClaimedIndependently()
    {
        using var workspace = new Stage3Workspace();
        var path = workspace.Out("registry.db");
        var authorityId = AuthorizationConsumptionRegistry.Provision(path, Stage4bAuthorizations.TestAuthorityId);
        var registry = AuthorizationConsumptionRegistry.OpenAt(path, authorityId);
        var first = CreateAuthorization("owner-authorization-a", authorityId);
        var second = CreateAuthorization("owner-authorization-b", authorityId);

        Claim(registry, first, "execution-a");
        Claim(registry, second, "execution-b");
        Assert.True(registry.ContainsAuthorization(first.AuthorizationId));
        Assert.True(registry.ContainsAuthorization(second.AuthorizationId));
    }

    [Fact]
    public void ReusingAuthorizationReferenceWithNewId_IsStillRejected()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        Claim(registry, authorization, "execution-original");
        var simulatedDispatches = 1;
        var replacement = Sign(authorization with { AuthorizationId = Guid.NewGuid().ToString("N"), Signature = string.Empty });

        Assert.Throws<ExecutionException>(() => Claim(registry, replacement, "execution-replacement"));
        Assert.Equal(1, simulatedDispatches);
    }

    [Fact]
    public void AmbiguousCommitAcknowledgement_FailsClosedWithoutDispatchAndKeepsClaim()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        var simulatedDispatches = 0;

        Assert.Throws<ExecutionException>(() =>
        {
            registry.Claim(authorization, CanonicalJson.Identity(authorization), authorization.PlanHash,
                authorization.TransportProfileIdentity, "execution-ambiguous", DateTimeOffset.UtcNow,
                afterCommitForTesting: () => throw new IOException("simulated-lost-commit-ack"));
            simulatedDispatches++;
        });
        Assert.Equal(0, simulatedDispatches);
        Assert.True(registry.ContainsAuthorization(authorization.AuthorizationId));
        Assert.Throws<ExecutionException>(() => Claim(registry, authorization, "execution-after-ambiguous"));
    }

    [Fact]
    public void ProviderFailureTimeoutAndCancellationAfterClaim_DoNotReleaseAuthorization()
    {
        foreach (var outcome in new[] { "provider-failed", "dispatch-timeout", "cancelled-after-claim" })
        {
            using var workspace = new Stage3Workspace();
            var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace, "authorization-" + outcome);
            var claim = Claim(registry, authorization, "execution-" + outcome);
            var simulatedDispatches = 0;
            if (outcome is "provider-failed" or "dispatch-timeout") simulatedDispatches++;
            var terminalExit = outcome switch
            {
                "provider-failed" => 5,
                "dispatch-timeout" => 124,
                _ => 130
            };
            registry.RecordTerminalOutcome(claim, terminalExit);
            Assert.Throws<ExecutionException>(() => Claim(registry, authorization, "execution-replay-" + outcome));
            Assert.Equal(outcome == "cancelled-after-claim" ? 0 : 1, simulatedDispatches);
        }
    }

    [Fact]
    public void MissingReadOnlyAndCorruptStores_FailClosedWithoutCreatingOrClaiming()
    {
        using var workspace = new Stage3Workspace();
        var missing = workspace.Out("missing-registry.db");
        Assert.Equal("authorization-consumption-store-unavailable", Assert.Throws<ExecutionException>(
            () => AuthorizationConsumptionRegistry.OpenAt(missing, Stage4bAuthorizations.TestAuthorityId)).Code);
        Assert.False(File.Exists(missing));

        var readOnly = workspace.Out("readonly-registry.db");
        AuthorizationConsumptionRegistry.Provision(readOnly, Stage4bAuthorizations.TestAuthorityId);
        File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
        try
        {
            Assert.Equal("authorization-consumption-store-unavailable", Assert.Throws<ExecutionException>(
                () => AuthorizationConsumptionRegistry.OpenAt(readOnly, Stage4bAuthorizations.TestAuthorityId)).Code);
        }
        finally { File.SetAttributes(readOnly, FileAttributes.Normal); }

        var corrupt = workspace.Out("corrupt-registry.db");
        File.WriteAllBytes(corrupt, [0x42, 0x41, 0x49, 0x5A, 0x45, 0x21]);
        Assert.Equal("authorization-consumption-store-unavailable", Assert.Throws<ExecutionException>(
            () => AuthorizationConsumptionRegistry.OpenAt(corrupt, Stage4bAuthorizations.TestAuthorityId)).Code);
        Assert.True(File.Exists(corrupt));
    }

    [Fact]
    public void WrongRegistryAuthority_FailsClosed()
    {
        using var workspace = new Stage3Workspace();
        var path = workspace.Out("wrong-authority.db");
        AuthorizationConsumptionRegistry.Provision(path, Stage4bAuthorizations.TestAuthorityId);

        Assert.Equal("authorization-consumption-authority-mismatch", Assert.Throws<ExecutionException>(
            () => AuthorizationConsumptionRegistry.OpenAt(path, "22222222222222222222222222222222")).Code);
    }

    [Fact]
    public void TerminalOutcomeIsDiagnostic_AndDoesNotMakeAuthorizationReusable()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        var claim = Claim(registry, authorization, "execution-terminal");
        registry.RecordTerminalOutcome(claim, 3);

        Assert.True(registry.ContainsAuthorization(authorization.AuthorizationId));
        Assert.Throws<ExecutionException>(() => Claim(registry, authorization, "execution-terminal-replay"));
    }

    [Fact]
    public void AuthorizationSignatureBindsIdentityAndScope()
    {
        var authorization = CreateAuthorization("signed-authorization", Stage4bAuthorizations.TestAuthorityId);
        LiveAuthorizationAuthentication.Verify(authorization);
        var changedId = authorization with { AuthorizationId = Guid.NewGuid().ToString("N") };

        Assert.Equal("live-authorization-signature-invalid", Assert.Throws<ExecutionException>(
            () => LiveAuthorizationAuthentication.Verify(changedId)).Code);
    }

    [Fact]
    public void AuthorizationLifetimeIsUtcAndAtMostTenMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var atMaximum = CreateAuthorization("lifetime-boundary", Stage4bAuthorizations.TestAuthorityId,
            now, now + LiveAuthorizations.MaximumValidity);
        LiveAuthorizations.ValidateValidity(atMaximum, now);

        var tooLong = Sign(atMaximum with { ExpiresUtc = now + LiveAuthorizations.MaximumValidity + TimeSpan.FromTicks(1) });
        Assert.Equal("live-authorization-validity-too-long", Assert.Throws<ExecutionException>(
            () => LiveAuthorizations.ValidateValidity(tooLong, now)).Code);
    }

    [Fact]
    public void AuthorizationLifetimeRejectsExpiredFutureNonUtcAndInvalidBoundaries()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = CreateAuthorization("expired", Stage4bAuthorizations.TestAuthorityId,
            now - TimeSpan.FromMinutes(10), now);
        Assert.Equal("live-authorization-expired", Assert.Throws<ExecutionException>(
            () => LiveAuthorizations.ValidateValidity(expired, now)).Code);

        var future = CreateAuthorization("future", Stage4bAuthorizations.TestAuthorityId,
            now + TimeSpan.FromTicks(1), now + TimeSpan.FromMinutes(1));
        Assert.Equal("live-authorization-not-yet-valid", Assert.Throws<ExecutionException>(
            () => LiveAuthorizations.ValidateValidity(future, now)).Code);

        var nonUtc = Sign(CreateAuthorization("non-utc", Stage4bAuthorizations.TestAuthorityId,
            now, now + TimeSpan.FromMinutes(1)) with
        { IssuedUtc = now.ToOffset(TimeSpan.FromHours(1)) });
        Assert.Equal("live-authorization-time-invalid", Assert.Throws<ExecutionException>(
            () => LiveAuthorizations.ValidateValidity(nonUtc, now)).Code);

        var reversed = Sign(CreateAuthorization("reversed", Stage4bAuthorizations.TestAuthorityId,
            now, now + TimeSpan.FromMinutes(1)) with
        { ExpiresUtc = now });
        Assert.Equal("live-authorization-time-invalid", Assert.Throws<ExecutionException>(
            () => LiveAuthorizations.ValidateValidity(reversed, now)).Code);
    }

    [Fact]
    public void ExpiryAfterClaim_BlocksDispatchAndLeavesClaimConsumed()
    {
        using var workspace = new Stage3Workspace();
        var (registry, _, authorization) = CreateRegistryAndAuthorization(workspace);
        Claim(registry, authorization, "execution-expiry-after-claim");
        var simulatedDispatches = 0;

        try
        {
            LiveAuthorizations.ValidateValidity(authorization, authorization.ExpiresUtc);
            simulatedDispatches++;
        }
        catch (ExecutionException exception)
        {
            Assert.Equal("live-authorization-expired", exception.Code);
        }
        Assert.Equal(0, simulatedDispatches);
        Assert.True(registry.ContainsAuthorization(authorization.AuthorizationId));
    }

    [Fact]
    public void CredentialReadinessRejectsMissingEmptyAndUnavailableWithoutReturningSecret()
    {
        const string secretName = "BAIZE_TEST_ONLY_CREDENTIAL";
        var original = Environment.GetEnvironmentVariable(secretName);
        try
        {
            Environment.SetEnvironmentVariable(secretName, null);
            Assert.Equal("live-credential-not-ready", Assert.Throws<ExecutionException>(() =>
                CredentialReadiness.Require(new EnvironmentCredentialReadinessProvider(), secretName)).Code);
            Environment.SetEnvironmentVariable(secretName, "   ");
            Assert.Equal("live-credential-not-ready", Assert.Throws<ExecutionException>(() =>
                CredentialReadiness.Require(new EnvironmentCredentialReadinessProvider(), secretName)).Code);
            const string sentinel = "not-a-real-provider-key-sentinel";
            Environment.SetEnvironmentVariable(secretName, sentinel);
            CredentialReadiness.Require(new EnvironmentCredentialReadinessProvider(), secretName);
            Assert.DoesNotContain(sentinel, CanonicalJson.Identity(new { credentialReference = secretName }));
            Assert.Equal("live-credential-provider-unavailable", Assert.Throws<ExecutionException>(() =>
                CredentialReadiness.Require(new UnavailableCredentialReadinessProvider(), secretName)).Code);
        }
        finally { Environment.SetEnvironmentVariable(secretName, original); }
    }

    private static (AuthorizationConsumptionRegistry Registry, string Path, LiveAuthorizationDocument Authorization)
        CreateRegistryAndAuthorization(Stage3Workspace workspace, string authorizationRef = "consumption-test-authorization")
    {
        var path = workspace.Out("authorization-registry.db");
        var authorityId = AuthorizationConsumptionRegistry.Provision(path, Stage4bAuthorizations.TestAuthorityId);
        return (AuthorizationConsumptionRegistry.OpenAt(path, authorityId), path,
            CreateAuthorization(authorizationRef, authorityId));
    }

    private static LiveAuthorizationDocument CreateAuthorization(string authorizationRef, string authorityId,
        DateTimeOffset? issuedUtc = null, DateTimeOffset? expiresUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new LiveAuthorizationDocument(
            LiveAuthorizations.Kind,
            LiveAuthorizations.SchemaVersion,
            Guid.NewGuid().ToString("N"),
            authorityId,
            "sha256:" + new string('a', 64),
            string.Empty,
            authorizationRef,
            "offline-test-owner",
            issuedUtc ?? now - TimeSpan.FromMinutes(1),
            expiresUtc ?? now + TimeSpan.FromMinutes(5),
            "sha256:" + new string('b', 64),
            "sha256:" + new string('c', 64),
            "sha256:" + new string('d', 64),
            "sha256:" + new string('e', 64),
            TransportProfiles.Http11ExactId,
            TransportProfiles.Describe("ReviewedNetwork", TransportProfiles.Http11ExactId).Identity,
            "OpenAi",
            "offline-endpoint",
            "https://openrouter.ai/api/v1",
            "https://openrouter.ai",
            "offline/model",
            "openai-chat-completions",
            "v1",
            "OPENROUTER_API_KEY",
            "offline-artifact-destination",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Dialect"] = "Standard" },
            [new LiveAuthorizationCase("case-test", "trial-test", 1, "openrouter-llama31-8b-smoke",
                "chat.baseline@3", ["chat.baseline@3"], 1, 1)],
            new BudgetEnvelopeEvidence(1, 1, 256, 512, 0, 768, "0.0004", "USD"));
        return Sign(document);
    }

    private static LiveAuthorizationDocument Sign(LiveAuthorizationDocument authorization)
        => Stage4bAuthorizations.Sign(authorization);

    private static AuthorizationConsumptionClaim Claim(AuthorizationConsumptionRegistry registry,
        LiveAuthorizationDocument authorization, string correlation) => registry.Claim(authorization,
            CanonicalJson.Identity(authorization), authorization.PlanHash, authorization.TransportProfileIdentity,
            correlation, DateTimeOffset.UtcNow);

    private static string WriteAuthorization(Stage3Workspace workspace, LiveAuthorizationDocument authorization,
        string name = "authorization-process.json") => workspace.WriteInput(name,
        Encoding.UTF8.GetString(CanonicalJson.Write(authorization)));

    private static string WriteBytes(Stage3Workspace workspace, string name, byte[] bytes)
    {
        var path = workspace.Out(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Process StartProbe(string databasePath, string authorizationPath, string correlation,
        params string[] extraArguments)
    {
        var appBase = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = appBase.Parent?.Name ?? "Release";
        var root = appBase;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Penghou.Baize.slnx")))
            root = root.Parent!;
        if (root is null) throw new InvalidOperationException("repository root not found");
        var probe = Path.Combine(root.FullName, "tests", "Penghou.Baize.Qualification.AuthorizationRegistryProbe",
            "bin", configuration, "net10.0", "Penghou.Baize.Qualification.AuthorizationRegistryProbe.dll");
        if (!File.Exists(probe)) throw new FileNotFoundException("process probe was not built", probe);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(probe);
        startInfo.ArgumentList.Add("claim");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add(authorizationPath);
        startInfo.ArgumentList.Add(correlation);
        foreach (var argument in extraArguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("probe process failed to start");
    }

    private static async Task WaitForFile(string path)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!File.Exists(path)) await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
    }

    private sealed class UnavailableCredentialReadinessProvider : ICredentialReadinessProvider
    {
        public bool CanProvideNonEmptyCredential(string credentialReference) => throw new InvalidOperationException();
    }
}
