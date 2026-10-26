using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

internal static class Stage4Fixtures
{
    public static string Inventory => Read("inventory.json");
    public static string Policy => Read("policy.json");

    public static string Read(string fileName)
    {
        var assembly = typeof(Stage4Fixtures).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Stage4aFixtures." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}

internal static class Stage4bFixtures
{
    public static string Inventory => Read("inventory.json");
    public static string Policy => Read("policy.json");

    public static string Read(string fileName)
    {
        var assembly = typeof(Stage4bFixtures).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Stage4bFixtures." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}

internal sealed class Stage4Gate : IHttpAdmissionGate
{
    private readonly List<(string Phase, int? StatusCode, long BytesRead, bool Completed)> _results = [];
    private readonly TaskCompletionSource _headersReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _admissions;

    public int Admissions => Volatile.Read(ref _admissions);
    public int FailedResultRecords => 0;
    public Task HeadersReceived => _headersReceived.Task;

    public (string Phase, int? StatusCode, long BytesRead, bool Completed)[] Results
    {
        get { lock (_results) return _results.ToArray(); }
    }

    public Task<HttpAdmissionDecision> AdmitAsync(HttpDispatchRecord dispatch, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _admissions);
        return Task.FromResult(new HttpAdmissionDecision(true, null, dispatch.DispatchId));
    }

    public Task RecordResultAsync(HttpDispatchRecord dispatch, int? statusCode, string phase, long bytesRead,
        bool completed, CancellationToken cancellationToken)
    {
        lock (_results) _results.Add((phase, statusCode, bytesRead, completed));
        if (phase == "headers-received") _headersReceived.TrySetResult();
        return Task.CompletedTask;
    }
}

internal sealed class Stage4LoopbackHarness : IAsyncDisposable
{
    private readonly string _secretSlot;
    private readonly string? _previousSecret;

    private Stage4LoopbackHarness(ScriptedLoopbackServer server, Stage4Gate gate, CaseExecutor executor,
        string secretSlot, string? previousSecret)
    {
        Server = server;
        Gate = gate;
        Executor = executor;
        _secretSlot = secretSlot;
        _previousSecret = previousSecret;
    }

    public ScriptedLoopbackServer Server { get; }
    public Stage4Gate Gate { get; }
    public CaseExecutor Executor { get; }
    public string SecretSlot => _secretSlot;

    public static Stage4LoopbackHarness Create(LoopbackMode mode, string? redirectTarget = null)
    {
        var server = new ScriptedLoopbackServer(mode, redirectTarget);
        var slot = "LOOPBACK_TEST_SLOT_" + Guid.NewGuid().ToString("N");
        var inventoryJson = Stage4Fixtures.Inventory
            .Replace("http://127.0.0.1:51234/v1", $"http://127.0.0.1:{server.Port}/v1", StringComparison.Ordinal)
            .Replace("LOOPBACK_TEST_SLOT", slot, StringComparison.Ordinal);
        var inventory = CanonicalJson.Read<Inventory>(inventoryJson);
        var entry = inventory.Entries.Single(e => e.Id == "loopback-local-alpha");
        var gate = new Stage4Gate();
        var previous = Environment.GetEnvironmentVariable(slot);
        Environment.SetEnvironmentVariable(slot, "synthetic-not-a-credential");
        var executor = CaseExecutor.Create("loopback-local-alpha", entry, inventory, gate, ExecutionFaults.None);
        return new Stage4LoopbackHarness(server, gate, executor, slot, previous);
    }

    public async Task RunSemanticAsync(string attemptId, TimeSpan? attemptTimeout = null, TimeSpan? idleTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = attemptTimeout ?? TimeSpan.FromSeconds(10);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var lease = new AttemptLease(attemptId, "run", "case", "trial", 1, "loopback-local-alpha",
            "POST", Executor.DestinationOrigin, Executor.DestinationBasePath, _secretSlot,
            cancellationToken, deadline.Token, DateTimeOffset.UtcNow + timeout, idleTimeout ?? TimeSpan.FromSeconds(5),
            1 << 20, 4 << 20, 1);
        using var scope = AttemptLeaseScope.Enter(lease);
        var request = QualificationFixtures.CreateBaselineRequest();
        await foreach (var _ in Executor.Stream(request, cancellationToken).WithCancellation(cancellationToken))
        {
        }
    }

    public async Task WaitForConnectionAsync()
    {
        await Server.FirstConnection.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Executor.Dispose();
        Environment.SetEnvironmentVariable(_secretSlot, _previousSecret);
        await Server.DisposeAsync();
    }
}
