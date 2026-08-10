using FluentAssertions;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;

namespace Penghou.Baize.Tests;

public sealed class LlmCapabilitiesValidationTests
{
    private sealed class ValidationProbeClient(
        LlmEndpointCapabilities capabilities)
        : LlmClientBase(
            model: "probe",
            httpClientFactory: new TestHttpClientFactory(
                new HttpClient(new StubHandler())),
            apiKey: "test-key",
            capabilities: capabilities)
    {
        public ValidationProbeClient()
            : this(
                new LlmEndpointCapabilities
                {
                    NativeToolCalling = false,
                    ParallelToolCalls = false
                })
        {
        }

        public static IAsyncEnumerable<(string? EventType, string Data)> ReadSseAsync(
            Stream stream,
            CancellationToken cancellationToken) =>
            ReadSseEventsAsync(stream, cancellationToken);

        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request) =>
            new(HttpMethod.Post, "http://localhost");

        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task ReadSseAsync_PropagatesCancellationBeforeReading()
    {
        await using var stream = new MemoryStream("data: partial\n\n"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () =>
        {
            await foreach (var _ in ValidationProbeClient.ReadSseAsync(
                               stream,
                               cancellation.Token))
            {
            }
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Capabilities_AreQueryable()
    {
        var client = new ValidationProbeClient(
            new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                ParallelToolCalls = false
            });

        client.Capabilities.NativeToolCalling.Should().BeTrue();
        client.Capabilities.ParallelToolCalls.Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_RejectsToolCallReplayWhenNativeToolCallingUnsupported()
    {
        var client = new ValidationProbeClient();
        var request = new LlmRequest(
            [
                LlmMessage.Assistant(
                    [
                        new LlmToolCall(
                            "call_1",
                            "get_weather",
                            """{"city":"Paris"}""")
                    ])
            ]);

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                request,
                TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*replays assistant tool calls and/or tool results*");
    }

    [Fact]
    public async Task StreamAsync_RejectsToolResultReplayWhenNativeToolCallingUnsupported()
    {
        var client = new ValidationProbeClient();
        var request = new LlmRequest(
            [
                LlmMessage.ToolResult(
                    "call_1",
                    "get_weather",
                    """{"temp":21}""")
            ]);

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                request,
                TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*replays assistant tool calls and/or tool results*");
    }

    [Fact]
    public async Task StreamAsync_RejectsParallelToolCallsWhenUnsupported()
    {
        var client = new ValidationProbeClient(
            new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                ParallelToolCalls = false
            });
        var request = new LlmRequest(
            [
                LlmMessage.Assistant(
                    [
                        new LlmToolCall(
                            "call_1",
                            "get_weather",
                            """{"city":"Paris"}"""),
                        new LlmToolCall(
                            "call_2",
                            "get_time",
                            """{"city":"Paris"}""")
                    ])
            ]);

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                request,
                TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support parallel tool calls*");
    }

    private sealed class TestHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        string.Empty,
                        Encoding.UTF8,
                        "application/json")
                });
    }
}
