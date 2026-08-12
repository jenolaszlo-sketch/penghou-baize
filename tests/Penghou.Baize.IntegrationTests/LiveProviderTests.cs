using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Batch;
using Penghou.Baize.Router;
using System.Text.Json;

namespace Penghou.Baize.IntegrationTests;

public sealed class LiveProviderTests(ITestOutputHelper output)
{
    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.Baseline)]
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
                // Thinking-first models may consume part of this output
                // budget before producing visible content.
                maxTokens: 512),
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
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.Thinking)]
    public async Task ThinkingSmokeTest_ReportsThinkingUsageAndDiagnostics()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.ThinkingEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_THINKING=1 for the thinking test.");

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            thinking: true);
        var router = provider.GetRequiredService<ILlmRouter>();

        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [new LlmMessage("user", "What is 17 multiplied by 19?")],
                temperature: 0,
                maxTokens: 2048,
                thinkingConfig: new LlmThinkingConfig(
                    LlmThinkingMode.Enabled,
                    LlmThinkingEffort.Low)),
            TestContext.Current.CancellationToken);

        response.Content.Should().Contain("323");
        response.Usage.Should().NotBeNull();
        response.RouterDiagnostics.Should().NotBeNull();
        (response.Reasoning is { Length: > 0 } ||
         response.Usage.ThinkingTokens is > 0).Should().BeTrue(
            "an explicit thinking request should surface reasoning text or " +
            "provider-reported thinking-token usage");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"Usage={response.Usage} Diagnostics={response.Diagnostics}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.Tools)]
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
                // Gemini thinking tokens share the output budget with the
                // eventual function call.
                maxTokens: 512,
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

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ComplexTools)]
    public async Task ComplexToolSmokeTest_CompletesSequentialToolRoundTrip()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.ToolsEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_TOOLS=1 for tool tests.");

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(settings, tools: true);
        var router = provider.GetRequiredService<ILlmRouter>();
        var tools = CreateInventoryTools();
        var messages = new List<LlmMessage>
        {
            new(
                "user",
                "Manage SKU BAIZE-42. First look up its inventory. If its " +
                "quantity is below the target of 15, create a restock plan for " +
                "the exact difference using the warehouse returned by the " +
                "lookup. Once the plan is created, reply with only its plan ID. " +
                "Use the supplied tools and do not guess tool results.")
        };

        var lookupResponse = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(messages, temperature: 0, maxTokens: 1024, tools: tools),
            TestContext.Current.CancellationToken);
        var lookupCall = lookupResponse.ToolCalls
            .Should().ContainSingle().Subject;
        lookupCall.Name.Should().Be("lookup_inventory");
        using (var arguments = JsonDocument.Parse(lookupCall.ArgumentsJson))
        {
            arguments.RootElement.GetProperty("sku").GetString()
                .Should().Be("BAIZE-42");
        }

        messages.Add(ToAssistantMessage(lookupResponse));
        messages.Add(LlmMessage.ToolResult(
            lookupCall.Id,
            lookupCall.Name,
            """{"sku":"BAIZE-42","quantity":7,"warehouse":"MNL"}"""));

        var restockResponse = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(messages, temperature: 0, maxTokens: 1024, tools: tools),
            TestContext.Current.CancellationToken);
        var restockCall = restockResponse.ToolCalls
            .Should().ContainSingle().Subject;
        restockCall.Name.Should().Be("create_restock_plan");
        using (var arguments = JsonDocument.Parse(restockCall.ArgumentsJson))
        {
            arguments.RootElement.GetProperty("sku").GetString()
                .Should().Be("BAIZE-42");
            arguments.RootElement.GetProperty("warehouse").GetString()
                .Should().Be("MNL");
            arguments.RootElement.GetProperty("amount").GetInt32()
                .Should().Be(8);
        }

        messages.Add(ToAssistantMessage(restockResponse));
        messages.Add(LlmMessage.ToolResult(
            restockCall.Id,
            restockCall.Name,
            """{"planId":"PLAN-9","status":"created"}"""));

        var finalResponse = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(messages, temperature: 0, maxTokens: 1024, tools: tools),
            TestContext.Current.CancellationToken);

        finalResponse.ToolCalls.Should().BeNullOrEmpty();
        finalResponse.Content.Trim().Should().Be("PLAN-9");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"LookupRepair={lookupCall.JsonWasRepaired} " +
            $"RestockRepair={restockCall.JsonWasRepaired} " +
            $"FinalUsage={finalResponse.Usage} " +
            $"FinalDiagnostics={finalResponse.Diagnostics}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ParallelTools)]
    public async Task ParallelToolSmokeTest_ReturnsTwoCallsInOneTurn()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.ToolsEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_TOOLS=1 for tool tests.");

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: true,
            parallelTools: true);
        var router = provider.GetRequiredService<ILlmRouter>();
        var tools = CreateParallelLookupTools();
        var messages = new List<LlmMessage>
        {
            new(
                "user",
                "Obtain the current weather for MNL and the USD to PHP exchange " +
                "rate. These lookups are independent: call get_weather and " +
                "get_exchange_rate together in the same response before waiting " +
                "for either result. Do not call any other tool. After both results " +
                "arrive, reply exactly as: MNL {temperatureC}C USD/PHP {rate}")
        };

        var callsResponse = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(messages, temperature: 0, maxTokens: 1024, tools: tools),
            TestContext.Current.CancellationToken);
        callsResponse.ToolCalls.Should().NotBeNull();
        var calls = callsResponse.ToolCalls!;
        calls.Should().HaveCount(2);
        calls.Select(call => call.Name).Should().BeEquivalentTo(
            ["get_weather", "get_exchange_rate"]);

        var weatherCall = calls.Single(call => call.Name == "get_weather");
        using (var arguments = JsonDocument.Parse(weatherCall.ArgumentsJson))
        {
            arguments.RootElement.GetProperty("city").GetString()
                .Should().Be("MNL");
        }

        var rateCall = calls.Single(call => call.Name == "get_exchange_rate");
        using (var arguments = JsonDocument.Parse(rateCall.ArgumentsJson))
        {
            arguments.RootElement.GetProperty("baseCurrency").GetString()
                .Should().Be("USD");
            arguments.RootElement.GetProperty("quoteCurrency").GetString()
                .Should().Be("PHP");
        }

        messages.Add(ToAssistantMessage(callsResponse));
        messages.Add(LlmMessage.ToolResults(
            [
                new LlmToolResult(
                    weatherCall.Id,
                    weatherCall.Name,
                    """{"city":"MNL","temperatureC":31}"""),
                new LlmToolResult(
                    rateCall.Id,
                    rateCall.Name,
                    """{"baseCurrency":"USD","quoteCurrency":"PHP","rate":57.25}""")
            ]));

        var finalResponse = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(messages, temperature: 0, maxTokens: 1024, tools: tools),
            TestContext.Current.CancellationToken);

        finalResponse.ToolCalls.Should().BeNullOrEmpty();
        finalResponse.Content.Trim().Should().Be("MNL 31C USD/PHP 57.25");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"CallCount={calls.Count} " +
            $"WeatherRepair={weatherCall.JsonWasRepaired} " +
            $"RateRepair={rateCall.JsonWasRepaired} " +
            $"FinalUsage={finalResponse.Usage} " +
            $"FinalDiagnostics={finalResponse.Diagnostics}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.StructuredOutput)]
    public async Task StructuredOutputSmokeTest_ReturnsSchemaValidJson()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            nativeStructuredOutput:
                string.Equals(
                    settings.Provider,
                    "OpenAi",
                    StringComparison.OrdinalIgnoreCase));
        var router = provider.GetRequiredService<ILlmRouter>();
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [new LlmMessage(
                    "user",
                    "Return value baize-live and count 3 in the requested JSON shape.")],
                temperature: 0,
                maxTokens: 512,
                responseFormat: LlmResponseFormat.JsonSchema(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "value": { "type": "string" },
                        "count": { "type": "integer" }
                      },
                      "required": ["value", "count"],
                      "additionalProperties": false
                    }
                    """)),
            TestContext.Current.CancellationToken);

        using var content = JsonDocument.Parse(response.Content);
        content.RootElement.GetProperty("value").GetString()
            .Should().Be("baize-live");
        content.RootElement.GetProperty("count").GetInt32()
            .Should().Be(3);
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine(
            $"Captured HTTP diagnostics: {settings.DiagnosticsDirectory}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.Batch)]
    public async Task NativeBatchSmokeTest_CompletesAndCorrelatesResult()
    {
        var settings = LiveTestSettings.Load();
        if (!LiveTestSettings.BatchEnabled)
            Assert.Skip("Set BAIZE_LIVE_TEST_BATCH=1 for the native batch test.");

        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(settings, tools: false);
        var batches = provider.GetRequiredService<IBaizeBatchCoordinator>();
        var logicalBatchId = $"baize-live-{Guid.NewGuid():N}";
        var handle = await batches.SubmitAsync(
            new BaizeBatchSubmission(
                [
                    BaizeBatchRequest.Create(
                        "batch-request-1",
                        "live",
                        new LlmRequest(
                            [new LlmMessage(
                                "user",
                                "Reply with the exact token BAIZE_BATCH_OK and no other text.")],
                            temperature: 0,
                            maxTokens: 512))
                ],
                Id: logicalBatchId,
                Metadata: new Dictionary<string, string>
                {
                    ["display_name"] = logicalBatchId
                }),
            TestContext.Current.CancellationToken);

        output.WriteLine(
            $"Submitted logical batch {handle.LogicalBatchId}; " +
            $"physical parts={handle.Parts.Count}.");
        var resultSet = await batches.WaitForResultsAsync(
            handle,
            new BatchWaitOptions
            {
                PollInterval = TimeSpan.FromSeconds(5),
                MaxPollInterval = TimeSpan.FromSeconds(30),
                BackoffFactor = 1.5,
                JitterRatio = 0,
                Timeout = TimeSpan.FromMinutes(15),
                Progress = new Progress<BatchPollingUpdate>(update =>
                    output.WriteLine(
                        $"Batch poll {update.PollNumber}: " +
                        $"state={update.Status?.State} " +
                        $"next={update.NextDelay} error={update.Error}"))
            },
            TestContext.Current.CancellationToken);

        resultSet.State.Should().Be(BaizeBatchState.Completed);
        var result = resultSet.Results.Should().ContainSingle().Subject;
        result.RequestId.Should().Be("batch-request-1");
        result.State.Should().Be(BaizeBatchItemState.Succeeded);
        result.Response.Should().NotBeNull();
        result.Response!.Content.Should().Contain("BAIZE_BATCH_OK");
        output.WriteLine(
            $"Batch completed: state={resultSet.State} " +
            $"finish={result.Response.FinishReason} usage={result.Response.Usage} " +
            $"diagnostics={result.Response.Diagnostics}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageInput)]
    public async Task ImageInputSmokeTest_IdentifiesDominantColor()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            inlineMediaType: LlmContentType.Image);
        var router = provider.GetRequiredService<ILlmRouter>();
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "solid-red.png.base64");
        var imageBytes = Convert.FromBase64String(
            (await File.ReadAllTextAsync(
                fixturePath,
                TestContext.Current.CancellationToken)).Trim());
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [
                    new LlmMessage(
                        "user",
                        [
                            new LlmTextContent(
                                "What is the single dominant color in this image? " +
                                "Reply with exactly RED, GREEN, or BLUE."),
                            new LlmImageContent(
                                "image/png",
                                new LlmInlineDataSource(imageBytes))
                        ])
                ],
                temperature: 0,
                maxTokens: 512),
            TestContext.Current.CancellationToken);

        response.Content.Trim().Should().BeEquivalentTo("RED");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine(
            $"Fixture={fixturePath} Bytes={imageBytes.Length}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.AudioInput)]
    public async Task AudioInputSmokeTest_DetectsAudibleTones()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            inlineMediaType: LlmContentType.Audio);
        var router = provider.GetRequiredService<ILlmRouter>();
        var audioBytes = WaveAudioFixture.CreateThreeBeeps();
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [
                    new LlmMessage(
                        "user",
                        [
                            new LlmTextContent(
                                "Classify the attached audio as containing audible " +
                                "tones or only silence. Reply with exactly TONES or " +
                                "SILENCE."),
                            new LlmAudioContent(
                                "audio/wav",
                                new LlmInlineDataSource(audioBytes))
                        ])
                ],
                temperature: 0,
                maxTokens: 512),
            TestContext.Current.CancellationToken);

        response.Content.Trim().Should().BeEquivalentTo("TONES");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine($"Audio=WAV/PCM16 Bytes={audioBytes.Length}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.VideoInput)]
    public async Task VideoInputSmokeTest_IdentifiesColorSequence()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            inlineMediaType: LlmContentType.Video);
        var router = provider.GetRequiredService<ILlmRouter>();
        var videoBytes = AviVideoFixture.CreateRedGreenBlueSequence();
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [
                    new LlmMessage(
                        "user",
                        [
                            new LlmVideoContent(
                                "video/avi",
                                new LlmInlineDataSource(videoBytes)),
                            new LlmTextContent(
                                "This video contains three solid-color sections. " +
                                "List their chronological order using exactly three " +
                                "uppercase color words separated by spaces.")
                        ])
                ],
                temperature: 0,
                maxTokens: 512),
            TestContext.Current.CancellationToken);

        response.Content.Trim().Should().BeEquivalentTo("RED GREEN BLUE");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine($"Video=AVI/RGB24 Bytes={videoBytes.Length} Duration=6s");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.FileInput)]
    public async Task FileInputSmokeTest_ExtractsPdfFields()
    {
        var settings = LiveTestSettings.Load();
        using var telemetry = new LiveTelemetryScope(output);
        await using var provider = LiveClientFactory.Create(
            settings,
            tools: false,
            inlineMediaType: LlmContentType.File);
        var router = provider.GetRequiredService<ILlmRouter>();
        var documentBytes = PdfDocumentFixture.Create();
        var response = await router.CompleteStreamingAsync(
            "live",
            new LlmRequest(
                [
                    new LlmMessage(
                        "user",
                        [
                            new LlmFileContent(
                                "application/pdf",
                                new LlmInlineDataSource(documentBytes),
                                "baize-live-document.pdf"),
                            new LlmTextContent(
                                "Read the attached document. Return its reference " +
                                "code followed by the sum of its two quantities, " +
                                "separated by one space. Return nothing else.")
                        ])
                ],
                temperature: 0,
                maxTokens: 512),
            TestContext.Current.CancellationToken);

        response.Content.Trim().Should().BeEquivalentTo("ORBIT-417 21");
        output.WriteLine(
            $"Provider={settings.Provider} Model={settings.Model} " +
            $"FinishReason={response.FinishReason} Usage={response.Usage} " +
            $"Diagnostics={response.Diagnostics}");
        output.WriteLine($"Document=PDF Bytes={documentBytes.Length}");
    }

    [Fact]
    [Trait(LiveTestTraits.Category, LiveTestTraits.Live)]
    [Trait(LiveTestTraits.Capability, LiveTestTraits.ImageGeneration)]
    public void ImageGenerationSmokeTest_RequiresGenerationClient()
    {
        Assert.Skip(
            "Image generation is reserved for the provider-neutral GenerationClient. " +
            "ILlmClient cannot represent generated binary media, and Gemini image " +
            "generation is unavailable on the free tier.");
    }

    private static List<LlmTool> CreateInventoryTools() =>
    [
        new LlmTool(
            "lookup_inventory",
            "Returns current inventory and warehouse for a SKU.",
            """
            {
              "type": "object",
              "properties": {
                "sku": { "type": "string" }
              },
              "required": ["sku"],
              "additionalProperties": false
            }
            """),
        new LlmTool(
            "create_restock_plan",
            "Creates a restock plan for a SKU at a warehouse.",
            """
            {
              "type": "object",
              "properties": {
                "sku": { "type": "string" },
                "warehouse": { "type": "string" },
                "amount": { "type": "integer" }
              },
              "required": ["sku", "warehouse", "amount"],
              "additionalProperties": false
            }
            """),
        new LlmTool(
            "lookup_supplier",
            "Returns contact details for a supplier ID.",
            """
            {
              "type": "object",
              "properties": {
                "supplierId": { "type": "string" }
              },
              "required": ["supplierId"],
              "additionalProperties": false
            }
            """)
    ];

    private static List<LlmTool> CreateParallelLookupTools() =>
    [
        new LlmTool(
            "get_weather",
            "Returns current weather for an airport or city code.",
            """
            {
              "type": "object",
              "properties": {
                "city": { "type": "string" }
              },
              "required": ["city"],
              "additionalProperties": false
            }
            """),
        new LlmTool(
            "get_exchange_rate",
            "Returns the current exchange rate for a currency pair.",
            """
            {
              "type": "object",
              "properties": {
                "baseCurrency": { "type": "string" },
                "quoteCurrency": { "type": "string" }
              },
              "required": ["baseCurrency", "quoteCurrency"],
              "additionalProperties": false
            }
            """),
        new LlmTool(
            "get_timezone",
            "Returns the time zone for a location.",
            """
            {
              "type": "object",
              "properties": {
                "location": { "type": "string" }
              },
              "required": ["location"],
              "additionalProperties": false
            }
            """)
    ];

    private static LlmMessage ToAssistantMessage(LlmResponse response)
    {
        if (response.Parts is { Count: > 0 })
            return new LlmMessage("assistant", response.Parts);

        return LlmMessage.Assistant(
            response.ToolCalls ?? [],
            string.IsNullOrWhiteSpace(response.Content) ? null : response.Content);
    }
}
