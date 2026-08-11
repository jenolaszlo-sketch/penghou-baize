using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;

namespace Penghou.Baize.Diagnostics.Tests;

public sealed class HttpTrafficCaptureHandlerTests
{
    [Fact]
    public async Task EnabledCapture_WritesBoundedRedactedArtifactsAndTelemetry()
    {
        var directory = CreateTemporaryDirectory();
        var measurements = new ConcurrentQueue<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BaizeTelemetry.InstrumentationName &&
                    instrument.Name.StartsWith("baize.diagnostics", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
            measurements.Enqueue(instrument.Name));
        meterListener.Start();

        try
        {
            await using var provider = CreateProvider(
                directory,
                enabled: true,
                maxBodyBytes: 5,
                new StubHandler("abcdefghij"));
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://example.test/chat?key=top-secret&upload_id=upload-secret&mode=test")
            {
                Content = new StringContent("hello", Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    "top-secret");
            request.Headers.Add("x-goog-api-key", "google-secret");

            using var response = await factory.CreateClient("llm").SendAsync(
                request,
                TestContext.Current.CancellationToken);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Be("abcdefghij");

            var requestLog = File.ReadAllText(
                Directory.GetFiles(directory, "*.request.log").Single());
            var responseLog = File.ReadAllText(
                Directory.GetFiles(directory, "*.response.log").Single());
            var rawResponse = File.ReadAllText(
                Directory.GetFiles(directory, "*.response.raw").Single());

            requestLog.Should().Contain("key=[REDACTED]");
            requestLog.Should().Contain("upload_id=[REDACTED]");
            requestLog.Should().Contain("mode=test");
            requestLog.Should().Contain("Authorization: [REDACTED]");
            requestLog.Should().Contain("x-goog-api-key: [REDACTED]");
            requestLog.Should().NotContain("top-secret");
            requestLog.Should().NotContain("google-secret");
            requestLog.Should().NotContain("upload-secret");
            rawResponse.Should().Be("abcde");
            responseLog.Should().Contain("Truncated: True");
            measurements.Should().Contain("baize.diagnostics.sessions");
            measurements.Should().Contain("baize.diagnostics.captured_bytes");
            measurements.Should().Contain("baize.diagnostics.truncated_bodies");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisabledCapture_HasNoFilesystemSideEffects()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"baize-diagnostics-{Guid.NewGuid():N}");
        await using var provider = CreateProvider(
            directory,
            enabled: false,
            maxBodyBytes: 10,
            new StubHandler("response"));

        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("llm")
            .GetAsync("https://example.test/chat", TestContext.Current.CancellationToken);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("response");
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSetupFailure_DoesNotBreakInferenceByDefault()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var provider = CreateProvider(
                path,
                enabled: true,
                maxBodyBytes: 10,
                new StubHandler("response"));

            using var response = await provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("llm")
                .GetAsync("https://example.test/chat", TestContext.Current.CancellationToken);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Be("response");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CaptureActivity_ContainsNoRequestOrResponseContent()
    {
        var directory = CreateTemporaryDirectory();
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

        try
        {
            await using var provider = CreateProvider(
                directory,
                enabled: true,
                maxBodyBytes: 100,
                new StubHandler("private-response"));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://example.test/activity?upload_id=upload-secret")
            {
                Content = new StringContent("private-prompt")
            };
            using var response = await provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("llm")
                .SendAsync(request, TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            var matchingActivities = activities.Where(item =>
                item.OperationName == "llm.http.capture" &&
                item.GetTagItem("url.full")?.ToString()?.Contains(
                    "/activity",
                    StringComparison.Ordinal) == true).ToArray();
            var activity = matchingActivities.Should().ContainSingle().Subject;
            activity.Tags.Select(tag => tag.Value).Should()
                .NotContain(["private-prompt", "private-response", "upload-secret"]);
            activity.GetTagItem("url.full")?.ToString()
                .Should().Contain("upload_id=[REDACTED]");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticClientLogging_DoesNotLogProviderPayloads()
    {
        var logs = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(new RecordingLoggerProvider(logs)));
        services.AddBaizeHttpDiagnostics();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var decorator = provider.GetServices<ILlmClientDecorator>().Single();
        var client = decorator.Decorate(new FailingClient());

        var action = async () =>
        {
            await foreach (var _ in client.StreamAsync(
                               new LlmRequest([new LlmMessage("user", "private-prompt")]),
                               TestContext.Current.CancellationToken))
            {
            }
        };

        await action.Should().ThrowAsync<LlmClientException>();
        logs.Should().Contain(message => message.Contains(
            "Baize stream failed",
            StringComparison.Ordinal));
        string.Join(Environment.NewLine, logs).Should()
            .NotContain("private-provider-payload")
            .And.NotContain("private-prompt");
    }

    [Fact]
    public async Task Registration_IsIdempotent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            void Configure(HttpTrafficCaptureOptions options)
            {
                options.Enabled = true;
                options.DirectoryPath = directory;
            }

            services.AddBaizeHttpDiagnostics(Configure);
            services.AddBaizeHttpDiagnostics(Configure);
            services.AddHttpClient("llm")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler("response"));
            await using var provider =
                services.BuildServiceProvider(validateScopes: true);

            using var response = await provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("llm")
                .GetAsync("https://example.test/chat", TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Directory.GetFiles(directory, "*.request.log").Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Registration_BindsTheDefaultConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Baize:Diagnostics:Enabled"] = "true",
                ["Baize:Diagnostics:DirectoryPath"] = "diagnostic-output",
                ["Baize:Diagnostics:MaxBodyBytes"] = "1234"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBaizeHttpDiagnostics(configuration);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var options = provider.GetRequiredService<
            IOptionsMonitor<HttpTrafficCaptureOptions>>().CurrentValue;
        options.Enabled.Should().BeTrue();
        options.DirectoryPath.Should().Be("diagnostic-output");
        options.MaxBodyBytes.Should().Be(1234);
    }

    private static ServiceProvider CreateProvider(
        string directory,
        bool enabled,
        long maxBodyBytes,
        HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBaizeHttpDiagnostics(options =>
        {
            options.Enabled = enabled;
            options.DirectoryPath = directory;
            options.MaxBodyBytes = maxBodyBytes;
            options.MaxRetainedSessions = 10;
        });
        services.AddHttpClient("llm")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"baize-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
    }

    private sealed class FailingClient : ILlmClient, ILlmClientMetadataProvider
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public LlmClientMetadata Metadata { get; } =
            new("Test", "test-model", EndpointId: "test-endpoint");

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new LlmClientException("private-provider-payload", 500);
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class RecordingLoggerProvider(
        ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            new RecordingLogger(messages);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(
        ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
