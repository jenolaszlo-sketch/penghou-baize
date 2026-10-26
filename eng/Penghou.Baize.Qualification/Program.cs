using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

return await QualificationCli.ExecuteAsync(args, Console.Out, Console.Error, ExecutionFaults.None,
    new OfflineBoundary(), CancellationToken.None);

internal static class QualificationCli
{
    // Stage 1 synchronous offline entry point, preserved for the existing
    // acceptance suite. Execution commands use ExecuteAsync.
    public static int Execute(string[] args, TextWriter output, TextWriter error, OfflineBoundary boundary)
    {
        try
        {
            return ExecuteOffline(args, output, error, boundary);
        }
        catch (SimulatedProcessCrashException)
        {
            throw;
        }
    }

    public static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error,
        ExecutionFaults faults, OfflineBoundary boundary, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length > 0 && args[0] == "run")
                return await RunCommand.RunAsync(args[1..], output, error, faults, cancellationToken).ConfigureAwait(false);
            if (args.Length > 0 && args[0] == "run-live")
                return await RunCommand.RunLiveAsync(args[1..], output, error, faults, cancellationToken).ConfigureAwait(false);
            if (args.Length > 0 && args[0] == "validate-live")
                return await RunCommand.ValidateLiveAsync(args[1..], output, error, faults, cancellationToken).ConfigureAwait(false);
            if (args.Length > 0 && args[0] == "resume")
                return await RunCommand.ResumeAsync(args[1..], output, error, faults, cancellationToken).ConfigureAwait(false);
            if (args.Length > 0 && args[0] == "report")
                return await ReportCommand.ExecuteAsync(args[1..], output, error, cancellationToken).ConfigureAwait(false);
            return ExecuteOffline(args, output, error, boundary);
        }
        catch (SimulatedProcessCrashException)
        {
            // Simulated process death: nothing may be reported as a completed
            // qualification. The durable stores retain the partial history.
            throw;
        }
    }

    private static int ExecuteOffline(string[] args, TextWriter output, TextWriter error, OfflineBoundary boundary)
    {
        try
        {
            var inventoryCommand = args.Length >= 2 && args[0] == "inventory" && args[1] == "validate";
            Validation.Require(inventoryCommand || args.Length > 0 && args[0] == "plan", "unknown-command");
            var allowed = inventoryCommand ? new[] { "--inventory" } : new[] { "--inventory", "--policy", "--models", "--contracts", "--out" };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = inventoryCommand ? 2 : 1; i < args.Length; i += 2)
            {
                Validation.Require(i + 1 < args.Length && allowed.Contains(args[i], StringComparer.Ordinal)
                    && !string.IsNullOrWhiteSpace(args[i + 1]) && options.TryAdd(args[i], args[i + 1]), "invalid-options");
            }
            Validation.Require(options.Count == allowed.Length, "missing-options");
            var planner = new Planner(boundary);
            var inventory = ReadBounded(options["--inventory"]);
            if (inventoryCommand)
            {
                output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(planner.ValidateInventory(inventory))));
                return 0;
            }
            var plan = planner.Generate(inventory, ReadBounded(options["--policy"]), Select(options["--models"]), Select(options["--contracts"]), BuildBinding.Collect());
            // Stage 1 artifacts are create-new planning files, not an admission store
            // or authoritative journal. The directory must not already exist.
            var directory = Path.GetFullPath(options["--out"]);
            Validation.Require(!Directory.Exists(directory) && !File.Exists(directory), "output-already-exists");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "plan.json"), plan.Content);
            File.WriteAllBytes(Path.Combine(directory, "identity.json"), CanonicalJson.Write(new
            {
                kind = "qualification-plan-identity",
                schemaVersion = "1.0",
                canonicalWriter = CanonicalJson.Version,
                planHash = plan.Hash,
                executionAuthorized = false
            }));
            output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
            {
                kind = "plan-generated",
                schemaVersion = "1.0",
                planHash = plan.Hash,
                commandCompleted = true,
                qualificationPerformed = false,
                executionAuthorized = false,
                liveEligibility = "Blocked"
            })));
            // Offline generation succeeded even though execution is blocked; the
            // explicit summary can never be interpreted as a qualification result.
            return 0;
        }
        catch (PlanningException exception) { return Fail(exception.Code, 2, output, error); }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException
            or OverflowException or FileNotFoundException or DirectoryNotFoundException)
        { return Fail("invalid-authoritative-input", 2, output, error); }
        catch (Exception) { return Fail("evidence-or-internal-failure", 5, output, error); }
    }

    private static string[] Select(string value) => value.Split(',', StringSplitOptions.TrimEntries);
    private static string ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        Validation.Require(stream.Length <= 1 << 20, "input-too-large");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var content = reader.ReadToEnd();
        Validation.Require(Encoding.UTF8.GetByteCount(content) <= 1 << 20, "input-too-large");
        return content;
    }
    private static int Fail(string code, int exitCode, TextWriter output, TextWriter error)
    {
        // Never echo user selectors, paths, parser diagnostics or exception text.
        error.WriteLine(code);
        output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
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
}
