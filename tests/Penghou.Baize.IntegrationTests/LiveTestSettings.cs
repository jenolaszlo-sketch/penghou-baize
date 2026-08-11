namespace Penghou.Baize.IntegrationTests;

internal sealed record LiveTestSettings(
    string Provider,
    string ProviderAssembly,
    string Model,
    string? SecretName,
    string? BaseUrl,
    string DiagnosticsDirectory)
{
    public static LiveTestSettings Load()
    {
        if (!IsTrue(Environment.GetEnvironmentVariable("BAIZE_RUN_LIVE_TESTS")))
            Assert.Skip("Set BAIZE_RUN_LIVE_TESTS=1 to run paid/live model tests.");

        var provider = Required("BAIZE_LIVE_PROVIDER");
        var model = Required("BAIZE_LIVE_MODEL");
        var (assembly, defaultSecret) = provider.ToUpperInvariant() switch
        {
            "OPENAI" => ("Penghou.Baize.OpenAi", "OPENAI_API_KEY"),
            "CLAUDE" => ("Penghou.Baize.Claude", "ANTHROPIC_API_KEY"),
            "GEMINI" => ("Penghou.Baize.Gemini", "GEMINI_API_KEY"),
            "OLLAMA" => ("Penghou.Baize.Ollama", null),
            _ => throw new InvalidOperationException(
                $"Unsupported BAIZE_LIVE_PROVIDER '{provider}'.")
        };
        var secret = Environment.GetEnvironmentVariable("BAIZE_LIVE_SECRET_NAME") ??
                     defaultSecret;
        if (secret is not null &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(secret)))
        {
            throw new InvalidOperationException(
                $"Live tests require secret environment variable '{secret}'.");
        }

        var diagnosticsDirectory =
            Environment.GetEnvironmentVariable("BAIZE_DIAGNOSTICS_DIRECTORY") ??
            Path.Combine(AppContext.BaseDirectory, "artifacts", "live-diagnostics");
        return new LiveTestSettings(
            provider,
            assembly,
            model,
            secret,
            Environment.GetEnvironmentVariable("BAIZE_LIVE_BASE_URL"),
            diagnosticsDirectory);
    }

    public static bool ToolsEnabled =>
        IsTrue(Environment.GetEnvironmentVariable("BAIZE_LIVE_TEST_TOOLS"));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Live tests require environment variable '{name}'.");

    private static bool IsTrue(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
