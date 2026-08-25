using FluentAssertions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;

namespace Penghou.Baize.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public async Task Client_EmitsOpenTelemetryActivitiesAndMetricsWithoutPromptContent()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == BaizeTelemetry.InstrumentationName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new ConcurrentQueue<(
            long Value,
            KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BaizeTelemetry.InstrumentationName &&
                    instrument.Name == "baize.llm.requests")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Enqueue((value, tags.ToArray())));
        meterListener.Start();

        var client = new TelemetryClient();
        await foreach (var _ in client.StreamAsync(
                           new LlmRequest([
                               new LlmMessage("user", "private prompt")
                           ]),
                           TestContext.Current.CancellationToken))
        {
        }

        var activity = activities.Should().ContainSingle(item =>
            item.OperationName == "llm.stream" &&
            Equals(item.GetTagItem("gen_ai.request.model"), "test-model")).Subject;
        activity.GetTagItem("gen_ai.operation.name").Should().Be("chat");
        activity.GetTagItem("gen_ai.provider.name").Should().Be("Test");
        activity.GetTagItem("gen_ai.request.model").Should().Be("test-model");
        activity.Tags.Select(tag => tag.Value)
            .Should().NotContain("private prompt");
        measurements.Should().Contain(item =>
            item.Value == 1 &&
            item.Tags.Any(tag =>
                tag.Key == "gen_ai.provider.name" &&
                Equals(tag.Value, "Test")));
    }

    [Fact]
    public async Task ClientFailure_DoesNotPutProviderPayloadInTelemetry()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == BaizeTelemetry.InstrumentationName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        var client = new FailureTelemetryClient();

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                               new LlmRequest([new LlmMessage("user", "private-prompt")]),
                               TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should().ThrowAsync<LlmClientException>();
        var activity = activities.Should().ContainSingle(item =>
            item.OperationName == "llm.stream" &&
            Equals(item.GetTagItem("gen_ai.request.model"), "failing-model")).Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().BeNull();
        activity.Tags.Select(tag => tag.Value).Should()
            .NotContain("private-provider-payload")
            .And.NotContain("private-prompt");
    }

    [Fact]
    public async Task CallerCancellation_DoesNotIncrementFailureMetric()
    {
        var failureCount = 0L;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BaizeTelemetry.InstrumentationName &&
                    instrument.Name == "baize.llm.failures")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "gen_ai.request.model" &&
                    Equals(tag.Value, "cancel-model"))
                {
                    Interlocked.Add(ref failureCount, value);
                    break;
                }
            }
        });
        meterListener.Start();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new CancellationTelemetryClient();

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                               new LlmRequest([new LlmMessage("user", "cancel")]),
                               cancellation.Token))
            {
            }
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
        failureCount.Should().Be(0);
    }

    private sealed class TelemetryClient()
        : LlmClientBase(
            "test-model",
            new TestHttpClientFactory(),
            string.Empty,
            new LlmEndpointCapabilities(),
            "Test")
    {
        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request) =>
            new(HttpMethod.Post, "https://example.test/chat");

        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(
                FinishReason: "stop",
                Usage: new LlmUsage(2, 1, 3));
        }
    }

    private sealed class FailureTelemetryClient()
        : LlmClientBase(
            "failing-model",
            new ErrorHttpClientFactory(),
            string.Empty,
            new LlmEndpointCapabilities(),
            "Test")
    {
        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request) =>
            new(HttpMethod.Post, "https://example.test/chat");

        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class CancellationTelemetryClient()
        : LlmClientBase(
            "cancel-model",
            new TestHttpClientFactory(),
            string.Empty,
            new LlmEndpointCapabilities(),
            "Test")
    {
        protected override HttpRequestMessage CreateHttpRequest(LlmRequest request) =>
            new(HttpMethod.Post, "https://example.test/chat");

        protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new TestHandler());
    }

    private sealed class ErrorHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ErrorHandler());
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            });
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("private-provider-payload")
            });
    }
}
