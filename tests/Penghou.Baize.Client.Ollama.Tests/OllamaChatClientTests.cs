using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.Ollama;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Ollama.Tests;

public sealed class OllamaChatClientTests
{
    [Fact]
    public async Task StreamAsync_PreservesLeadingWhitespaceAndTwentyCharacterTail()
    {
        const string expected = "\nhead12345678901234567890";
        var handler = new RecordingHandler(
            """
            {"model":"qwen","message":{"role":"assistant","content":"\nhead"},"done":false}
            {"model":"qwen","message":{"role":"assistant","content":"12345678901234567890"},"done":true,"done_reason":"stop"}
            """);
        var client = CreateClient(handler, "qwen");

        var response = await client.StreamAsync(
                new LlmRequest([new LlmMessage("user", "Reply")]),
                TestContext.Current.CancellationToken)
            .CollectAsync(cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be(expected);
        response.Content[^20..].Should().Be("12345678901234567890");
    }

    [Fact]
    public async Task StreamAsync_MapsSchemaLessJsonFormat()
    {
        var handler = new RecordingHandler(
            """{"model":"qwen","message":{"role":"assistant","content":"{}"},"done":true,"done_reason":"stop"}""");
        var client = CreateClient(handler, "qwen");

        await CollectAsync(client.StreamAsync(
            new LlmRequest(
                [new LlmMessage("user", "Return JSON")],
                responseFormat: LlmResponseFormat.Json()),
            TestContext.Current.CancellationToken));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        document.RootElement.GetProperty("format").GetString()
            .Should().Be("json");
    }

    [Fact]
    public async Task StreamAsync_MapsNativeToolCallAndUsage()
    {
        var handler = new RecordingHandler(
            """
            {"model":"qwen2.5-coder:7b","message":{"role":"assistant","content":"","tool_calls":[{"type":"function","function":{"index":0,"name":"emit_files","arguments":{"files":[]}}}]},"done":false}
            {"model":"qwen2.5-coder:7b","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","total_duration":2500000000,"load_duration":500000000,"prompt_eval_count":11,"prompt_eval_duration":250000000,"eval_count":7,"eval_duration":1000000000}
            """);
        var client = CreateClient(
            handler,
            "qwen2.5-coder:7b");
        var request = new LlmRequest(
            [new LlmMessage("user", "Generate files")],
            temperature: 0.1,
            maxTokens: 2048,
            tools:
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    """
                    {
                      "type": "object",
                      "properties": {
                        "files": { "type": "array" }
                      },
                      "required": ["files"]
                    }
                    """)
            ]);

        var events = await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current
                    .CancellationToken));

        var toolCall = events
            .Single(item =>
                item.ToolCallDelta is not null)
            .ToolCallDelta!;
        toolCall.Index.Should().Be(0);
        toolCall.Name.Should().Be(
            "emit_files");
        var arguments =
            toolCall.ArgumentsJsonFragment;
        using var argumentsDocument =
            JsonDocument.Parse(arguments!);
        argumentsDocument.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(0);
        events.Single(item =>
                item.Usage is not null)
            .Usage.Should().Be(
                new LlmUsage(11, 7, 18));
        events.Last().FinishReason
            .Should().Be("stop");
        events.Last().Diagnostics.Should().Be(
            new LlmProviderDiagnostics(
                Provider: "Ollama",
                ActualModel: "qwen2.5-coder:7b",
                Api: "native",
                Done: true,
                DoneReason: "stop",
                TotalDurationMilliseconds: 2500,
                LoadDurationMilliseconds: 500,
                PromptEvaluationDurationMilliseconds: 250,
                GenerationDurationMilliseconds: 1000,
                GenerationTokensPerSecond: 7,
                NativeToolCallCount: 1,
                ContentLength: 0));

        handler.RequestUri.Should().Be(
            new Uri(
                "http://ollama:11434/api/chat"));
        using var requestDocument =
            JsonDocument.Parse(
                handler.RequestBody!);
        requestDocument.RootElement
            .GetProperty("model")
            .GetString()
            .Should()
            .Be("qwen2.5-coder:7b");
        requestDocument.RootElement
            .GetProperty("stream")
            .GetBoolean()
            .Should()
            .BeTrue();
        requestDocument.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("object");
        requestDocument.RootElement
            .GetProperty("options")
            .GetProperty("num_predict")
            .GetInt32()
            .Should()
            .Be(2048);
    }

    [Fact]
    public async Task StreamAsync_MapsNativeContentResponse()
    {
        var handler = new RecordingHandler(
            """
            {"model":"granite4.1:3b","message":{"role":"assistant","content":"hel"},"done":false}
            {"model":"granite4.1:3b","message":{"role":"assistant","content":"lo"},"done":false}
            {"model":"granite4.1:3b","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}
            """);
        var client = CreateClient(
            handler,
            "granite4.1:3b");

        var events = await CollectAsync(
            client.StreamAsync(
                new LlmRequest(
                    [new LlmMessage("user", "hello")]),
                TestContext.Current
                    .CancellationToken));

        events.Where(item =>
                item.Delta != null)
            .Select(item => item.Delta)
            .Should()
            .Equal("hel", "lo");
        events.Last().FinishReason
            .Should().Be("stop");
    }

    [Fact]
    public async Task CompleteAsync_MapsStreamingThinkingAsOrderedReasoning()
    {
        var handler = new RecordingHandler(
            """
            {"model":"deepseek-r1","message":{"role":"assistant","thinking":"check "},"done":false}
            {"model":"deepseek-r1","message":{"role":"assistant","thinking":"carefully"},"done":false}
            {"model":"deepseek-r1","message":{"role":"assistant","content":"answer"},"done":false}
            {"model":"deepseek-r1","message":{"role":"assistant","content":"!"},"done":true,"done_reason":"stop"}
            """);
        var client = CreateClient(handler, "deepseek-r1");

        var response = await client.CompleteAsync(
            new LlmRequest([new LlmMessage("user", "solve")]),
            TestContext.Current.CancellationToken);

        response.Reasoning.Should().Be("check carefully");
        response.Content.Should().Be("answer!");
        response.Parts.Should().HaveCount(2);
        response.Parts[0].Should().BeOfType<LlmReasoningContent>()
            .Which.Text.Should().Be("check carefully");
        response.Parts[1].Should().BeOfType<LlmTextContent>()
            .Which.Text.Should().Be("answer!");
        response.FinishReason.Should().Be("stop");
    }

    [Fact]
    public async Task StreamAsync_RejectsMalformedResponseChunk()
    {
        var handler = new RecordingHandler(
            """
            {"message":{"role":"assistant","content":"partial"},"done":false}
            not-json
            """);
        var client = CreateClient(
            handler,
            "qwen2.5-coder:7b");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage(
                            "user",
                            "hello")]),
                    TestContext.Current
                        .CancellationToken));

        await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage(
                "*Failed to parse Ollama chat response chunk*");
    }

    [Fact]
    public async Task StreamAsync_RejectsStreamWithoutFinalChunk()
    {
        var handler = new RecordingHandler(
            """
            {"message":{"role":"assistant","content":"partial"},"done":false}
            """);
        var client = CreateClient(
            handler,
            "qwen2.5-coder:7b");

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    new LlmRequest(
                        [new LlmMessage(
                            "user",
                            "hello")]),
                    TestContext.Current
                        .CancellationToken));

        var exception = await action.Should()
            .ThrowAsync<LlmClientException>()
            .WithMessage(
                "*before a final chunk was received*");
        exception.Which.FailureKind
            .Should().Be(LlmClientFailureKind.Availability);
    }

    [Fact]
    public async Task StreamAsync_ThrowsForToolsWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            {"model":"gemma3:4b","message":{"role":"assistant","content":"ok"},"done":true}
            """);
        var client = CreateClient(
            handler,
            "gemma3:4b",
            DefaultCapabilities with { NativeToolCalling = false });
        var request = new LlmRequest(
            [new LlmMessage("user", "Generate files")],
            tools:
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    """{"type":"object"}""")
            ]);

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current
                        .CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support native tool calling*");
    }

    [Fact]
    public async Task StreamAsync_ThrowsForThinkingWhenUnsupported()
    {
        var handler = new RecordingHandler(
            """
            {"model":"gemma3:4b","message":{"role":"assistant","content":"ok"},"done":true}
            """);
        var client = CreateClient(
            handler,
            "gemma3:4b");
        var request = new LlmRequest(
            [new LlmMessage("user", "Reason")],
            thinkingConfig:
                new LlmThinkingConfig(
                    mode: LlmThinkingMode.Enabled));

        var action = async () =>
            await CollectAsync(
                client.StreamAsync(
                    request,
                    TestContext.Current
                        .CancellationToken));

        await action.Should()
            .ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support extended thinking*");
    }

    [Fact]
    public async Task StreamAsync_RejectsNonInlineImageBeforeSending()
    {
        var handler = new RecordingHandler(
            """{"model":"qwen","message":{"role":"assistant","content":""},"done":true}""");
        var client = CreateClient(handler, "qwen");
        var request = new LlmRequest([
            new LlmMessage("user", [
                new LlmImageContent(
                    "image/png",
                    new LlmUriSource(new Uri("https://cdn.test/image.png")))
            ])
        ]);

        var action = async () => await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        await action.Should().ThrowAsync<LlmRequestValidationException>()
            .WithMessage("*does not support*");
        handler.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task CompleteStreamingAsync_PreservesNativeDiagnostics()
    {
        var handler = new RecordingHandler(
            """
            {"model":"granite4.1:3b","message":{"role":"assistant","content":"incomplete result"},"done":true,"done_reason":"length","total_duration":131000000000,"load_duration":13300000000,"prompt_eval_count":872,"prompt_eval_duration":12400000000,"eval_count":1280,"eval_duration":105000000000}
            """);
        var client = CreateClient(
            handler,
            "granite4.1:3b");
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["granite-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("granite-native", ApiStyle.Ollama)] = () => client
                }),
            new Dictionary<
                ModelStrategy,
                IReadOnlyList<string>>());

        var response =
            await router.CompleteStreamingAsync(
                "granite-native",
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Generate")]
                },
                cancellationToken:
                    TestContext.Current
                        .CancellationToken);

        response.FinishReason.Should().Be("length");
        response.Usage.Should().Be(
            new LlmUsage(872, 1280, 2152));
        response.Diagnostics.Should().NotBeNull();
        response.Diagnostics!.ActualModel
            .Should().Be("granite4.1:3b");
        response.Diagnostics.DoneReason
            .Should().Be("length");
        response.Diagnostics.TotalDurationMilliseconds
            .Should().Be(131000);
        response.Diagnostics.GenerationTokensPerSecond
            .Should().BeApproximately(
                12.19,
                0.01);
        response.Diagnostics.NativeToolCallCount
            .Should().Be(0);
        response.Diagnostics.ContentLength
            .Should().Be(17);
    }

    [Fact]
    public void AddLlmRouting_MapsAliasToNativeOllamaClient()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LlmRouting:Models:0:Name"] =
                            "qwen-native",
                        ["LlmRouting:Models:0:Endpoints:0:ApiStyle"] =
                            "Ollama",
                        ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] =
                            "qwen2.5-coder:7b",
                        ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] =
                            "http://ollama:11434"
                    })
                .Build();
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddOllamaLlmProvider();
        services.AddLlmRouting(configuration);

        using var provider =
            services.BuildServiceProvider();
        var models = provider.GetRequiredService<ILlmModelLookup>();

        var client = models.GetClient("qwen-native");
        var metadata = client.Should()
            .BeAssignableTo<ILlmClientMetadataProvider>().Subject.Metadata;
        metadata.Provider.Should().Be("Ollama");
        metadata.Model.Should().Be("qwen2.5-coder:7b");
        metadata.Endpoint.Should().Be(new Uri("http://ollama:11434"));

        models.GetClient("qwen-native", ApiStyle.Ollama)
            .Should().BeSameAs(client);
    }

    [Fact]
    public async Task StreamAsync_RoundTripsToolCallConversation()
    {
        var handler = new RecordingHandler(
            """{"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}""");
        var client = CreateClient(handler, "qwen2.5-coder:7b");
        var request = new LlmRequest(
            [
                LlmMessage.Assistant(
                    [new LlmToolCall("call_1", "get_weather", """{"city":"Paris"}""")]),
                LlmMessage.ToolResults(
                    [new LlmToolResult("call_1", "get_weather", """{"temp":21}""")])
            ]);

        await CollectAsync(
            client.StreamAsync(
                request,
                TestContext.Current.CancellationToken));

        using var document =
            JsonDocument.Parse(handler.RequestBody!);
        var messages = document.RootElement
            .GetProperty("messages");
        messages.GetArrayLength().Should().Be(2);

        var function = messages[0]
            .GetProperty("tool_calls")[0]
            .GetProperty("function");
        messages[0].GetProperty("role")
            .GetString().Should().Be("assistant");
        function.GetProperty("name")
            .GetString().Should().Be("get_weather");
        function.GetProperty("arguments")
            .GetProperty("city")
            .GetString().Should().Be("Paris");

        messages[1].GetProperty("role")
            .GetString().Should().Be("tool");
        messages[1].GetProperty("content")
            .GetString().Should().Contain("temp");
    }

    private static OllamaChatClient CreateClient(
        RecordingHandler handler,
        string model,
        LlmEndpointCapabilities? capabilities = null) =>
        new(
            model,
            new TestHttpClientFactory(
                new HttpClient(handler)),
            apiKey: string.Empty,
            baseUrl:
                "http://ollama:11434",
            capabilities: capabilities ?? DefaultCapabilities);

    private static LlmEndpointCapabilities DefaultCapabilities =>
        new()
        {
            NativeToolCalling = true,
            ParallelToolCalls = true,
            NativeStructuredOutput = true,
            StructuredOutputViaTool = false,
            Thinking = false,
            ThinkingDisable = false,
            StreamingToolCallArguments = false
        };

    private static async Task<
        IReadOnlyList<LlmStreamEvent>>
        CollectAsync(
            IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events =
            new List<LlmStreamEvent>();

        await foreach (var item in stream)
            events.Add(item);

        return events;
    }

    private sealed class TestHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name) =>
            client;
    }

    private sealed class RecordingHandler(
        string responseBody)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<
            HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody =
                request.Content is null
                    ? null
                    : await request.Content
                        .ReadAsStringAsync(
                            cancellationToken);

            return new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
