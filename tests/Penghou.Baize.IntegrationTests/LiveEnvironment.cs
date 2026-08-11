namespace Penghou.Baize.IntegrationTests;

/// <summary>Loads a repository-local live-test environment without overriding CI values.</summary>
internal static class LiveEnvironment
{
    private static readonly object Sync = new();
    private static bool _loaded;

    public static void Load()
    {
        lock (Sync)
        {
            if (_loaded)
                return;

            _loaded = true;
            var path = FindEnvironmentFile();

            if (path is null)
                return;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line[7..].TrimStart();

                var separator = line.IndexOf('=');

                if (separator <= 0)
                    continue;

                var name = line[..separator].Trim();
                var value = Unquote(line[(separator + 1)..].Trim());

                if (name.Length > 0 && Environment.GetEnvironmentVariable(name) is null)
                    Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static string? FindEnvironmentFile()
    {
        var explicitPath = Environment.GetEnvironmentVariable("BAIZE_ENV_FILE");

        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

        return FindInAncestors(Directory.GetCurrentDirectory()) ??
               FindInAncestors(AppContext.BaseDirectory);
    }

    private static string? FindInAncestors(string start)
    {
        for (var directory = new DirectoryInfo(start);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".env.local");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 &&
        ((value[0] == '"' && value[^1] == '"') ||
         (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
