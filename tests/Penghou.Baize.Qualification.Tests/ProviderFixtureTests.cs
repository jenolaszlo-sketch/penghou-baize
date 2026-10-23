using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Qualification;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Qualification.Tests;

public sealed class ProviderFixtureTests
{
    private const string BaselineSse = """
        data: {"choices":[{"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        data: {"choices":[{"delta":{"content":"OK\r\n"},"finish_reason":"stop"}]}

        data: [DONE]


        """;

    [Fact]
    public async Task RealBaizeCollectorAndProviderTelemetry_QualifyControlledStream()
    {
        using var factory = new FakeHttpFactory(BaselineSse);
        var client = Client(factory);
        var result = await QualificationScenarios.RunBaselineAsync(new(client.StreamAsync), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, factory.Calls);
        Assert.Equal(11, result.Diagnostics.ContentCharacters);
        Assert.Equal(0, Assert.Single(result.Diagnostics.Integrity).BufferedCharacters);
        using var request = JsonDocument.Parse(factory.Request!);
        Assert.Equal(512, request.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task RealAdapterToolMappingUsesOneRequestAndCanonicalValidation()
    {
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":\"baize-live\"}"}}]},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]


            """;
        using var factory = new FakeHttpFactory(sse);
        var client = Client(factory);
        var result = await QualificationScenarios.RunSingleToolAsync(new(client.StreamAsync, NativeToolCalling: true), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task CoTerminalToolChunk_ExposesExistingAdapterOrderingFinding()
    {
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":\"baize-live\"}"}}]},"finish_reason":"tool_calls"}]}

            data: [DONE]


            """;
        using var factory = new FakeHttpFactory(sse);
        var client = Client(factory);
        var result = await QualificationScenarios.RunSingleToolAsync(new(client.StreamAsync, NativeToolCalling: true), TestContext.Current.CancellationToken);
        QualificationScenarioTests.AssertCheck(result, "chat.streaming-integrity@1", "stream.sequence", AssertionStatus.Failed, "invalid-event-sequence");
        Assert.False(result.Succeeded);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task RealAdapterMissingTerminalDoesNotBecomeSuccessfulContent()
    {
        using var factory = new FakeHttpFactory("data: {\"choices\":[{\"delta\":{\"content\":\"BAIZE_OK\"},\"finish_reason\":null}]}\n\n");
        var client = Client(factory);
        var result = await QualificationScenarios.RunBaselineAsync(new(client.StreamAsync), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        QualificationScenarioTests.AssertCheck(result, "chat.baseline@2", "execution.completed", AssertionStatus.Failed);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task RoutedCompositionPreservesDiagnosticsAndDoesNotRetryFailure()
    {
        using var factory = new FakeHttpFactory(BaselineSse);
        await using var services = Routed(factory);
        var router = services.GetRequiredService<ILlmRouter>();
        var validation = await router.ValidateEndpointsAsync(TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded);
        Assert.Equal(0, factory.Calls);
        var result = await QualificationScenarios.RunBaselineAsync(new(
            (request, token) => router.StreamAsync("fixture", request, token), RequireRouterDiagnostics: true), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, JsonSerializer.Serialize(result));
        Assert.Equal(1, result.Diagnostics.RouterAttemptCount);
        Assert.Equal(1, factory.Calls);

        using var failedFactory = new FakeHttpFactory("private provider error", HttpStatusCode.ServiceUnavailable);
        await using var failedServices = Routed(failedFactory);
        var failedRouter = failedServices.GetRequiredService<ILlmRouter>();
        result = await QualificationScenarios.RunBaselineAsync(new(
            (request, token) => failedRouter.StreamAsync("fixture", request, token), RequireRouterDiagnostics: true), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal(1, failedFactory.Calls);
        Assert.Equal(1, result.Diagnostics.RouterAttemptCount);
        Assert.DoesNotContain("private provider error", JsonSerializer.Serialize(result));
    }

    private static OpenAiChatClient Client(IHttpClientFactory factory) => new("fixture", factory,
        "FAKE_CREDENTIAL", "https://fixture.invalid/v1", new() { NativeToolCalling = true });

    private static ServiceProvider Routed(FakeHttpFactory factory)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["LlmRouting:ProviderModules:0:Assembly"] = "Penghou.Baize.OpenAi",
                ["LlmRouting:Retry:MaximumAttempts"] = "1",
                ["LlmRouting:Models:0:Name"] = "fixture",
                ["LlmRouting:Models:0:Endpoints:0:Id"] = "fixture-endpoint",
                ["LlmRouting:Models:0:Endpoints:0:Provider"] = "OpenAi",
                ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "fixture-model",
                ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] = "https://fixture.invalid/v1"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddLlmRouting(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FakeHttpFactory(string body, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory, IDisposable
    {
        private readonly RecordingHandler _handler = new(body, status);
        public int Calls => _handler.Calls;
        public string? Request => _handler.Request;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        public void Dispose() => _handler.Dispose();
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Request { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
}
