using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Qualification;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Qualification.Tests;

public sealed class QualificationProviderV3Tests
{
    private const string Ollama = """
        {"model":"fixture","message":{"role":"assistant","tool_calls":[{"function":{"index":0,"name":"echo_value","arguments":{"value":"baize-live"}}}]},"done":false}
        {"model":"fixture","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

        """;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R2_OllamaUnboundRevisionCannotEstablishIdentityApplicability(bool suppliedId)
    {
        var body = suppliedId ? Ollama.Replace("\"function\":", "\"id\":\"provider_call_17\",\"function\":", StringComparison.Ordinal) : Ollama;
        using var factory = new FakeHttpFactory(body);
        var client = new OllamaChatClient("fixture", factory, "", "https://fixture.invalid", new() { NativeToolCalling = true });
        var result = await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true,
            IdentityProtocol: ToolIdentityProtocol.OllamaChat), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(CanonicalIdentityOrigin.CollectorAssigned, result.Identity.CanonicalOrigin);
        Assert.Equal(AssertionStatus.Passed, result.Identity.CanonicalCorrelation);
        Assert.Equal(IdentityApplicability.Undetermined, result.Identity.ProviderIdentityApplicability);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
        Assert.Equal("protocol-revision-unbound", result.Identity.ProviderIdentityCode);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.identity", AssertionStatus.Passed);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.schema", AssertionStatus.Passed);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.value", AssertionStatus.Passed);
        Assert.DoesNotContain("provider_call_17", JsonSerializer.Serialize(result));
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 3, false)]
    [InlineData(true, 3, false)]
    public async Task R1_RealRouterCannotReplaceProviderCompletionEvidence(bool tools, int shape, bool expected)
    {
        const string baseline = "data: {\"choices\":[{\"delta\":{\"content\":\"BAIZE_OK\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        using var factory = new FakeHttpFactory(tools ? ToolChunk("{\"value\":\"baize-live\"}", true, false) + Finish() : baseline);
        await using var services = Routed(factory);
        var router = services.GetRequiredService<ILlmRouter>();
        var context = new QualificationContextV3((request, token) => WithCompletionObservations(router.StreamAsync("fixture", request, token), shape, token),
            tools, RequireRouterDiagnostics: true, IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
        var result = tools
            ? await QualificationScenariosV3.RunSingleToolAsync(context, Token)
            : await QualificationScenariosV3.RunBaselineAsync(context, Token);
        Assert.Equal(expected, result.Succeeded);
        QualificationScenarioV3Tests.Check(result, tools ? "tools.single@3" : "chat.baseline@3", "execution.completed", expected ? AssertionStatus.Passed : AssertionStatus.Failed);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.terminal", expected ? AssertionStatus.Passed : AssertionStatus.Failed);
        QualificationScenarioV3Tests.Check(result, tools ? "tools.single@3" : "chat.baseline@3", "diagnostics.router", AssertionStatus.Passed);
        Assert.Single(result.Router.Snapshots);
        Assert.Equal(1, result.Router.SuccessfulAttempts);
        Assert.Equal(0, result.Router.FailedAttempts);
        Assert.True(result.Router.TerminalOrderValid);
        Assert.Equal(1, factory.Calls);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> WithCompletionObservations(IAsyncEnumerable<LlmStreamEvent> stream,
        int shape, [EnumeratorCancellation] CancellationToken token)
    {
        // Inject explicit public diagnostic observations around the real routed
        // adapter stream. No claim that OpenAI emits these fixture diagnostics.
        if (shape == 1) yield return new(Diagnostics: new("fixture", Done: false));
        await foreach (var evt in stream.WithCancellation(token))
        {
            // Insert after the adapter's own final Done=true observation and
            // before the router snapshot. An explicit true after the injected
            // false would correctly supersede it and invalidate the oracle.
            if (evt.RouterDiagnostics is not null)
            {
                bool?[] observations = shape switch
                {
                    0 => [false, null],
                    1 => [true],
                    2 => [true, null],
                    _ => [true, false, null]
                };
                foreach (var done in observations) yield return new(Diagnostics: new("fixture", Done: done));
            }
            yield return evt;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task D1_GeminiOptionalIdIsNotMisreportedAsProviderProvenance(bool supplied)
    {
        var function = supplied ? "\"id\":\"provider_call\"," : "";
        var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{" + function +
            "\"name\":\"echo_value\",\"args\":{\"value\":\"baize-live\"}}}]},\"finishReason\":\"STOP\"}]}\n\ndata: [DONE]\n\n";
        using var factory = new FakeHttpFactory(sse);
        var client = new GeminiChatClient("fixture", factory, "FAKE", "https://fixture.invalid", new() { NativeToolCalling = true });
        var result = await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true,
            IdentityProtocol: ToolIdentityProtocol.GeminiGenerateContent), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(CanonicalIdentityOrigin.NormalizedStream, result.Identity.CanonicalOrigin);
        Assert.Equal(IdentityApplicability.Applicable, result.Identity.ProviderIdentityApplicability);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
        Assert.Equal("wire-provenance-not-observed", result.Identity.ProviderIdentityCode);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAiRequiresOriginalStreamIdAndOneRequest(bool supplied)
    {
        var sse = ToolChunk("{\"value\":\"baize-live\"}", header: true, terminal: false, supplied) + Finish();
        using var factory = new FakeHttpFactory(sse);
        var client = new OpenAiChatClient("fixture", factory, "FAKE", "https://fixture.invalid/v1", new() { NativeToolCalling = true });
        var result = await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true,
            IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions), Token);
        Assert.Equal(supplied, result.Succeeded);
        Assert.Equal(AssertionStatus.NotObserved, result.Identity.ProviderIdentityStatus);
        Assert.Equal(1, factory.Calls);
        if (!supplied) QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.identity", AssertionStatus.Failed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task F1_CoTerminalWholeCallAndFinalFragmentStillExposeAdapterDefect(bool finalFragment, bool routed)
    {
        var sse = finalFragment
            ? ToolChunk("{\"value\":", header: true, terminal: false) + ToolChunk("\"baize-live\"}", header: false, terminal: true)
            : ToolChunk("{\"value\":\"baize-live\"}", header: true, terminal: true);
        sse += "data: [DONE]\n\n";
        using var factory = new FakeHttpFactory(sse);
        await using var services = Routed(factory);
        var router = services.GetRequiredService<ILlmRouter>();
        var client = new OpenAiChatClient("fixture", factory, "FAKE", "https://fixture.invalid/v1", new() { NativeToolCalling = true });
        var context = new QualificationContextV3(routed ? (request, token) => router.StreamAsync("fixture", request, token) : client.StreamAsync,
            true, RequireRouterDiagnostics: routed, IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions);
        var result = await QualificationScenariosV3.RunSingleToolAsync(context, Token);
        Assert.False(result.Succeeded);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.sequence", AssertionStatus.Failed, "invalid-event-sequence");
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.value", AssertionStatus.Passed);
        QualificationScenarioV3Tests.Check(result, "tools.single@3", "tool.schema", AssertionStatus.Passed);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task RoutedSuccessAnd503KeepSafeAccountingWithoutRetry()
    {
        const string baseline = "data: {\"choices\":[{\"delta\":{\"content\":\"BAIZE_OK\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        using var factory = new FakeHttpFactory(baseline);
        await using var services = Routed(factory);
        var router = services.GetRequiredService<ILlmRouter>();
        var validation = await router.ValidateEndpointsAsync(Token);
        Assert.True(validation.Succeeded);
        Assert.Equal(0, factory.Calls);
        var result = await QualificationScenariosV3.RunBaselineAsync(new((request, token) => router.StreamAsync("fixture", request, token), RequireRouterDiagnostics: true), Token);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, result.Router.SuccessfulAttempts);
        Assert.Equal(1, factory.Calls);
        using var failedFactory = new FakeHttpFactory("private error", HttpStatusCode.ServiceUnavailable);
        await using var failedServices = Routed(failedFactory);
        var failedRouter = failedServices.GetRequiredService<ILlmRouter>();
        result = await QualificationScenariosV3.RunBaselineAsync(new((request, token) => failedRouter.StreamAsync("fixture", request, token), RequireRouterDiagnostics: true), Token);
        Assert.All(result.Contracts, c => Assert.Equal(ContractDisposition.Blocked, c.Disposition));
        Assert.Equal(1, result.Router.FailedAttempts);
        Assert.Equal(0, result.Router.SuccessfulAttempts);
        QualificationScenarioV3Tests.Check(result, "chat.streaming-integrity@2", "stream.dispatches", AssertionStatus.NotObserved);
        Assert.Equal(1, failedFactory.Calls);
        Assert.DoesNotContain("private error", JsonSerializer.Serialize(result));
    }

    private static string ToolChunk(string arguments, bool header, bool terminal, bool suppliedId = true)
    {
        var function = new Dictionary<string, object?> { ["arguments"] = arguments };
        var tool = new Dictionary<string, object?> { ["index"] = 0, ["function"] = function };
        if (header)
        {
            function["name"] = "echo_value";
            if (suppliedId) tool["id"] = "call_1";
            tool["type"] = "function";
        }
        return "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] { tool } }, finish_reason = terminal ? "tool_calls" : null } } }) + "\n\n";
    }

    private static string Finish() => "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\ndata: [DONE]\n\n";

    private static ServiceProvider Routed(FakeHttpFactory factory)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LlmRouting:ProviderModules:0:Assembly"] = "Penghou.Baize.OpenAi",
            ["LlmRouting:Retry:MaximumAttempts"] = "1",
            ["LlmRouting:Models:0:Name"] = "fixture",
            ["LlmRouting:Models:0:Endpoints:0:Id"] = "fixture-endpoint",
            ["LlmRouting:Models:0:Endpoints:0:Provider"] = "OpenAi",
            ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "fixture-model",
            ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] = "https://fixture.invalid/v1",
            ["LlmRouting:Models:0:Endpoints:0:Capabilities:NativeToolCalling"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddLlmRouting(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FakeHttpFactory(string body, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory, IDisposable
    {
        private readonly Handler _handler = new(body, status);
        public int Calls => _handler.Calls;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        public void Dispose() => _handler.Dispose();
        private sealed class Handler(string body, HttpStatusCode status) : HttpMessageHandler
        {
            public int Calls { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });
            }
        }
    }
}
