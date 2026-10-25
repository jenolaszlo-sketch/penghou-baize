using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;
using Penghou.Siming;

namespace Penghou.Baize.Qualification.Tests;

internal static class Stage2Fixtures
{
    public const string AllModels = "fake-alpha,fake-wrong,fake-toolbad,fake-no-terminal,fake-slow,fake-unavailable,fake-no-usage";
    public const string AllContracts = "chat.baseline@3,tools.single@3,chat.streaming-integrity@2";

    public static string Inventory => Read("inventory.json");
    public static string Policy => Read("policy.json");

    public static string Read(string fileName)
    {
        var assembly = typeof(Stage2Fixtures).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Stage2Fixtures." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}

internal sealed class Stage2Workspace : IDisposable
{
    private readonly string _root;

    public Stage2Workspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "baize-stage2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string PlanDir => Out("plan");
    public string RunDir => Out("run");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public string Out(string name) => Path.Combine(_root, name);

    public string WriteInput(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    public async Task<Stage2CommandResult> PlanAsync(string models, string contracts, string? inventory = null,
        string? policy = null, string? planDir = null, ExecutionFaults? faults = null)
    {
        var inventoryFile = WriteInput($"inventory-{Guid.NewGuid():N}.json", inventory ?? Stage2Fixtures.Inventory);
        var policyFile = WriteInput($"policy-{Guid.NewGuid():N}.json", policy ?? Stage2Fixtures.Policy);
        return await ExecuteAsync(faults ?? new ExecutionFaults(), TestContext.Current.CancellationToken,
            "plan", "--inventory", inventoryFile, "--policy", policyFile,
            "--models", models, "--contracts", contracts, "--out", planDir ?? PlanDir);
    }

    public Task<Stage2CommandResult> RunAsync(string models, string contracts, ExecutionFaults? faults = null,
        int trial = 1, string? runDir = null) =>
        ExecuteAsync(faults ?? new ExecutionFaults(), TestContext.Current.CancellationToken,
            "run", "--plan", Path.Combine(PlanDir, "plan.json"), "--out", runDir ?? RunDir,
            "--models", models, "--contracts", contracts, "--trial",
            trial.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task<Stage2CommandResult> RunWithCancellationAsync(string models, string contracts,
        CancellationToken cancellationToken, ExecutionFaults? faults = null) =>
        ExecuteAsync(faults ?? new ExecutionFaults(), cancellationToken,
            "run", "--plan", Path.Combine(PlanDir, "plan.json"), "--out", RunDir,
            "--models", models, "--contracts", contracts, "--trial", "1");

    public Task<Stage2CommandResult> ResumeAsync(ExecutionFaults? faults = null) =>
        ExecuteAsync(faults ?? new ExecutionFaults(), TestContext.Current.CancellationToken, "resume", "--run", RunDir);

    public Task<Stage2CommandResult> ReportAsync() =>
        ExecuteAsync(new ExecutionFaults(), TestContext.Current.CancellationToken, "report", "--run", RunDir);

    public static async Task<Stage2CommandResult> ExecuteAsync(ExecutionFaults faults,
        CancellationToken cancellationToken, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await QualificationCli.ExecuteAsync(args, output, error, faults, new OfflineBoundary(),
            cancellationToken);
        return new Stage2CommandResult(exitCode, output.ToString(), error.ToString());
    }
}

internal sealed class Stage2CommandResult
{
    public Stage2CommandResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public JsonElement Summary => JsonDocument.Parse(StandardOutput.Trim()).RootElement;

    public void AssertCompleted(string kind, int exitCode)
    {
        Assert.True(ExitCode == exitCode, $"exit {ExitCode} != {exitCode}; stderr: {StandardError}; stdout: {StandardOutput}");
        Assert.Equal(kind, Summary.GetProperty("kind").GetString());
        if (Summary.TryGetProperty("exitCode", out var recorded))
            Assert.Equal(exitCode, recorded.GetInt32());
    }

    public void AssertFailed(string code, int exitCode)
    {
        Assert.True(ExitCode == exitCode, $"exit {ExitCode} != {exitCode}; stderr: {StandardError}; stdout: {StandardOutput}");
        Assert.Equal(code, Summary.GetProperty("code").GetString());
        Assert.False(Summary.GetProperty("commandCompleted").GetBoolean());
    }
}

internal static class Stage2Evidence
{
    public static IReadOnlyList<LedgerEntry> ReadAll(Stage2Workspace workspace) => ReadAllDirectory(workspace.RunDir);

    public static IReadOnlyList<LedgerEntry> ReadAllDirectory(string runDirectory)
    {
        var entries = new List<LedgerEntry>();
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), ExecutionFaults.None);
        foreach (var entry in Enumerate(ledger).GetAwaiter().GetResult()) entries.Add(entry);
        ledger.DisposeAsync().GetAwaiter().GetResult();
        return entries;
    }

    private static async Task<List<LedgerEntry>> Enumerate(EvidenceLedger ledger)
    {
        var entries = new List<LedgerEntry>();
        await foreach (var entry in ledger.ReadAllAsync(cancellationToken: TestContext.Current.CancellationToken))
            entries.Add(entry);
        return entries;
    }

    public static JsonElement Event(IReadOnlyList<LedgerEntry> entries, string eventType, int index = 0)
    {
        var match = entries.Where(e => e.EventType == eventType).ElementAtOrDefault(index);
        Assert.NotNull(match);
        using var document = JsonDocument.Parse(match!.Payload);
        return document.RootElement.Clone();
    }

    public static JsonElement[] Events(IReadOnlyList<LedgerEntry> entries, string eventType) =>
        entries.Where(e => e.EventType == eventType).Select(e =>
        {
            using var document = JsonDocument.Parse(e.Payload);
            return document.RootElement.Clone();
        }).ToArray();

    public static int Count(IReadOnlyList<LedgerEntry> entries, string eventType) =>
        entries.Count(e => e.EventType == eventType);

    public static void AssertLedgerVerified(Stage2Workspace workspace) =>
        AssertLedgerVerifiedDirectory(workspace.RunDir);

    public static void AssertLedgerVerifiedDirectory(string runDirectory)
    {
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), ExecutionFaults.None);
        var verification = ledger.VerifyAsync(null).GetAwaiter().GetResult();
        ledger.DisposeAsync().GetAwaiter().GetResult();
        Assert.True(verification.IsValid, verification.Detail);
    }

    public static JsonElement Report(Stage2Workspace workspace) => ReportDirectory(workspace.RunDir);

    public static JsonElement ReportDirectory(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "reports", "summary.json");
        Assert.True(File.Exists(path), "report summary missing");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    public static string Markdown(Stage2Workspace workspace)
    {
        var path = Path.Combine(workspace.RunDir, "reports", "report.md");
        Assert.True(File.Exists(path), "report markdown missing");
        return File.ReadAllText(path);
    }
}
