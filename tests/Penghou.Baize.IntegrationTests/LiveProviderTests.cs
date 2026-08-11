using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Router;
using System.Text.Json;

namespace Penghou.Baize.IntegrationTests;

public sealed class LiveProviderTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task StreamingSmokeTest_ReturnsExpectedContentAndDiagnostics()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(settings, tools: false);
        var router = provider.GetRequiredService<ILlmRouter>();

        var validation = await router.ValidateEndpointsAsync(
            TestContext.Current.CancellationToken);
        validation.Succeeded.Should().BeTrue(
            string.Join(Environment.NewLine, validation.Endpoints.Select(result =>
                result.Error)));

        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [new LlmMessage(
                    "user",
                    "Reply with the exact token BAIZE_OK and no other text.")],
                temperature: 0,
                maxTokens: 64),
            TestContext.Current.CancellationToken);

        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine(
            $"Captured HTTP diagnostics: {settings.DiagnosticsDirectory}");
        response.Content.Should().Contain("BAIZE_OK");
        response.RouterDiagnostics.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Live")]
    [Trait("Capability", "Tools")]
    public async Task NativeToolSmokeTest_ReturnsSchemaValidArguments()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.ToolsEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_TOOLS=1 for the native tool test.");

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(settings, tools: true);
        var router = provider.GetRequiredService<ILlmRouter>();
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [new LlmMessage(
                    "user",
                    "Call echo_value exactly once with value baize-live. Do not answer in text.")],
                temperature: 0,
                maxTokens: 128,
                tools:
                [
                    new LlmTool(
                        "echo_value",
                        "Echoes the supplied value.",
                        """
                        {
                          "type": "object",
                          "properties": {
                            "value": { "type": "string" }
                          },
                          "required": ["value"],
                          "additionalProperties": false
                        }
                        """)
                ]),
            TestContext.Current.CancellationToken);

        var call = response.ToolCalls.Should().ContainSingle().Subject;
        call.Name.Should().Be("echo_value");
        using var arguments = JsonDocument.Parse(call.ArgumentsJson);
        arguments.RootElement.GetProperty("value").GetString()
            .Should().Be("baize-live");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} Tool={call.Name} " +
            $"Repair={response.ContentWasRepaired} " +
            $"Attempts={response.ContentRepairAttempts?.Count ?? 0}");
        output.WriteLine(
            $"Captured HTTP diagnostics: {settings.DiagnosticsDirectory}");
    }
}
