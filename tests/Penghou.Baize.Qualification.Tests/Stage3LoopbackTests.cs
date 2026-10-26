using System.Net;
using System.Net.Http;
using System.Text;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;
using Penghou.Baize.Router;

namespace Penghou.Baize.Qualification.Tests;

// The real HTTP stack over a local loopback listener: the actual Baize
// adapter, neutral transport, admission guard and SocketsHttpHandler compose
// against a controlled in-process backend. No provider endpoint is contacted.
public sealed class Stage3LoopbackTests
{
    private const string BaselineSse = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"OK\r\n"},"finish_reason":"stop"}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}

        data: [DONE]

        """;

    [Fact]
    public async Task LoopbackTerminal_ExercisesRealSocketsPath()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:51234/");
        listener.Start();
        try
        {
            var received = 0;
#pragma warning disable xUnit1051 // The loopback server observes the test token only for shutdown.
            var server = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var context = await listener.GetContextAsync();
                        Interlocked.Increment(ref received);
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                        var response = context.Response;
                        response.StatusCode = 200;
                        response.ContentType = "text/event-stream";
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(BaselineSse), TestContext.Current.CancellationToken);
                        response.Close();
                    }
                }
                catch (HttpListenerException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });
#pragma warning restore xUnit1051
            _ = server;
            var inventory = CanonicalJson.Read<Inventory>(Stage3Fixtures.Inventory);
            var entry = inventory.Entries.Single(e => e.Id == "loopback-local-alpha");
            var gate = new ObservingGate();
            var previous = Environment.GetEnvironmentVariable("LOOPBACK_TEST_SLOT");
            Environment.SetEnvironmentVariable("LOOPBACK_TEST_SLOT", "synthetic-not-a-credential");
            try
            {
                using var executor = CaseExecutor.Create("loopback-local-alpha", entry, inventory, gate, ExecutionFaults.None);
                var lease = new AttemptLease("loopback-attempt", "run", "case", "trial", 1, "loopback-local-alpha",
                    "POST", executor.DestinationOrigin, executor.DestinationBasePath, "LOOPBACK_TEST_SLOT",
                    CancellationToken.None, CancellationToken.None, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2),
                    TimeSpan.FromSeconds(30), 1 << 20, 4 << 20, 1);
                using var scope = AttemptLeaseScope.Enter(lease);
                var context = new QualificationContextV3(
                    (request, token) => executor.Stream(request, token),
                    NativeToolCalling: true, RequireRouterDiagnostics: true,
                    IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
                var result = await QualificationScenariosV3.RunBaselineAsync(context, TestContext.Current.CancellationToken);
                Assert.True(result.Succeeded, System.Text.Json.JsonSerializer.Serialize(result));
                Assert.Equal(1, gate.Admissions);
                Assert.True(executor.SecretReads >= 1, "the lease-aware provider must resolve the slot exactly once");
                Assert.Equal(0, executor.SecretLeaseDenials);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LOOPBACK_TEST_SLOT", previous);
            }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    // The lease-aware provider resolves credentials only inside a live lease:
    // outside the attempt scope no environment secret is ever read.
    [Fact]
    public async Task LeaseAwareSecretProvider_DeniesOutsideLease()
    {
        var provider = new LeaseAwareSecretProvider(new EnvironmentSecretProvider());
        var value = await provider.GetSecretAsync("LOOPBACK_TEST_SLOT", TestContext.Current.CancellationToken);
        Assert.Null(value);
        Assert.Equal(0, provider.SecretReads);
        Assert.Equal(1, provider.LeaseDenials);
    }

    private sealed class ObservingGate : IHttpAdmissionGate
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
}

// 15. No credentials, private prompts or raw tool arguments leak into default
// evidence, even when a real-shaped sentinel secret is configured.
public sealed class Stage3PrivacyTests
{
    [Fact]
    public async Task SentinelAndPayloads_NeverPersist()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha,fake-toolbad", "chat.baseline@3,tools.single@3"))
            .AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha,fake-toolbad", "chat.baseline@3,tools.single@3"))
            .AssertCompleted("run-summary", 3);
        foreach (var file in Directory.EnumerateFiles(workspace.RunDir, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)))
        {
            if (file.EndsWith("workflow.db", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("evidence.db", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = ReadShared(file);
                Assert.False(Contains(bytes, "BAIZE_OK"u8), $"content persisted in {file}");
                Assert.False(Contains(bytes, "baize-live"u8), $"tool argument persisted in {file}");
                Assert.False(Contains(bytes, "synthetic-not-a-credential"u8), $"sentinel persisted in {file}");
                Assert.False(Contains(bytes, "Call echo_value"u8), $"prompt persisted in {file}");
            }
            else
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("BAIZE_OK", text);
                Assert.DoesNotContain("baize-live", text);
                Assert.DoesNotContain("synthetic-not-a-credential", text);
                Assert.DoesNotContain("Call echo_value", text);
            }
        }
    }

    // A real profile is refused by the offline run command before any credential
    // resolution or dispatch, even with --allow-live.
    [Fact]
    public async Task OfflineRunRefusal_ReadsNoSecrets()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("deepseek-native-chat", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        var previous = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sentinel-credential-must-not-be-read");
        try
        {
            (await workspace.RunAsync("deepseek-native-chat", "chat.baseline@3"))
                .AssertFailed("live-execution-requires-run-live-command", 2);
            var refused = await workspace.RunAsync("deepseek-native-chat", "chat.baseline@3", allowLive: true);
            Assert.Equal(2, refused.ExitCode);
            Assert.Equal("command-failed", refused.Summary.GetProperty("kind").GetString());
            Assert.Equal("live-execution-requires-run-live-command", refused.Summary.GetProperty("code").GetString());
            Assert.False(Directory.Exists(workspace.RunDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previous);
        }
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return true;
        return false;
    }
}
