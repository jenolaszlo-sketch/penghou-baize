using System.Net;
using System.Net.Http;
using FluentAssertions;
using Penghou.Baize.Generation;
using Penghou.Baize.Generation.TestShared;

namespace Penghou.Baize.Runway.Tests;

/// <summary>
/// Per-model request timeouts: the wrapper stamps the timeout onto every
/// client the provider obtains, while the shared transport stays untouched
/// for consumers that did not opt in.
/// </summary>
public sealed class RequestTimeoutTests
{
    [Fact]
    public async Task WithRequestTimeout_IsAppliedToClientsUsedBySubmissions()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-t"}""");
        var shared = new HttpClient(handler);
        var inner = new TestHttpClientFactory(shared);
        var factory = BaizeHttp.WithRequestTimeout(inner, TimeSpan.FromSeconds(7));
        var client = new RunwayGenerationClient(
            model: "gen4.5",
            factory,
            apiKey: "secret",
            new Uri("https://api.dev.runwayml.com/v1"),
            VideoCapabilities(),
            "runway-gen-1");

        await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog running" },
            TestContext.Current.CancellationToken);

        // The wrapper mutates each client handed out by CreateClient; the
        // test factory always returns the same instance, so it now carries
        // the configured per-model timeout.
        inner.CreateClient("llm").Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task WithoutWrapper_SharedTransportDefaultStays()
    {
        var handler = new RecordingHandler().ReturnJson("""{"id":"task-t"}""");
        var shared = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(97)
        };
        var inner = new TestHttpClientFactory(shared);
        var client = new RunwayGenerationClient(
            model: "gen4.5",
            inner,
            apiKey: "secret",
            new Uri("https://api.dev.runwayml.com/v1"),
            VideoCapabilities(),
            "runway-gen-1");

        await client.SubmitAsync(
            new VideoGenerationRequest { Prompt = "a dog running" },
            TestContext.Current.CancellationToken);

        inner.CreateClient("llm").Timeout.Should().Be(TimeSpan.FromSeconds(97));
    }

    private static GenerationCapabilities VideoCapabilities() => new()
    {
        Features =
            GenerationFeature.TextToVideo |
            GenerationFeature.ImageToVideo |
            GenerationFeature.OperationRetrieval |
            GenerationFeature.Cancellation |
            GenerationFeature.Progress,
        InputTransports = new HashSet<LlmContentTransport>
        {
            LlmContentTransport.Uri,
            LlmContentTransport.InlineData,
            LlmContentTransport.ProviderFile
        }
    };
}
