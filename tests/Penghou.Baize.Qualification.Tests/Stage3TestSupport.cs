using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

internal static class Stage3Fixtures
{
    public static string Inventory => Read("inventory.json");
    public static string Policy => Read("policy.json");

    public static string Read(string fileName)
    {
        var assembly = typeof(Stage3Fixtures).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Stage3Fixtures." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}

internal sealed class Stage3Workspace : IDisposable
{
    private readonly string _root;

    public Stage3Workspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "baize-stage3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string PlanDir => Out("plan");
    public string RunDir => Out("run");
    public string Out(string name) => Path.Combine(_root, name);

    public string WriteInput(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    public async Task<Stage2CommandResult> PlanAsync(string models, string contracts, string? inventory = null,
        string? policy = null)
    {
        var inventoryFile = WriteInput($"inventory-{Guid.NewGuid():N}.json", inventory ?? Stage3Fixtures.Inventory);
        var policyFile = WriteInput($"policy-{Guid.NewGuid():N}.json", policy ?? Stage3Fixtures.Policy);
        return await ExecuteAsync(new ExecutionFaults(), "plan", "--inventory", inventoryFile, "--policy", policyFile,
            "--models", models, "--contracts", contracts, "--out", PlanDir);
    }

    public Task<Stage2CommandResult> RunAsync(string models, string contracts, bool allowLive = false,
        ExecutionFaults? faults = null, int trial = 1) =>
        ExecuteAsync(faults ?? new ExecutionFaults(), allowLive
            ? ["run", "--allow-live", "--plan", Path.Combine(PlanDir, "plan.json"), "--out", RunDir,
                "--models", models, "--contracts", contracts, "--trial", trial.ToString(System.Globalization.CultureInfo.InvariantCulture)]
            : ["run", "--plan", Path.Combine(PlanDir, "plan.json"), "--out", RunDir,
                "--models", models, "--contracts", contracts, "--trial", trial.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

    public Task<Stage2CommandResult> ResumeAsync(ExecutionFaults? faults = null) =>
        ExecuteAsync(faults ?? new ExecutionFaults(), "resume", "--run", RunDir);

    public Task<Stage2CommandResult> ReportAsync() =>
        ExecuteAsync(new ExecutionFaults(), "report", "--run", RunDir);

    public static async Task<Stage2CommandResult> ExecuteAsync(ExecutionFaults faults, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await QualificationCli.ExecuteAsync(args, output, error, faults, new OfflineBoundary(),
            TestContext.Current.CancellationToken);
        return new Stage2CommandResult(exitCode, output.ToString(), error.ToString());
    }

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
}
