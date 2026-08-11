using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize.Tools.Extensions;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class StructuredOutputRepairDecoratorTests
{
    [Fact]
    public async Task Decorator_RepairsSchemaConstrainedContentAndReportsDiagnostics()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmStructuredOutputRepair();
        using var provider = services.BuildServiceProvider();
        var decorator = provider.GetServices<ILlmClientDecorator>().Single();
        var client = decorator.Decorate(new EventClient(
            new LlmStreamEvent(Delta: "{\"name\":\"Ada\""),
            new LlmStreamEvent(FinishReason: "stop")));
        var request = new LlmRequest(
            [new LlmMessage("user", "Return JSON")],
            responseFormat: LlmResponseFormat.JsonSchema(
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""));

        var events = await CollectAsync(client.StreamAsync(
            request,
            TestContext.Current.CancellationToken));

        var content = string.Concat(events.Select(item => item.Delta));
        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("name").GetString().Should().Be("Ada");
        events.Should().Contain(item => item.ContentWasRepaired);
        events.SelectMany(item => item.ContentRepairAttempts ?? [])
            .Should().NotBeEmpty();
    }

    [Fact]
    public async Task Decorator_DoesNotBufferOrdinaryStreamingRequest()
    {
        var repairer = new ThrowingRepairer();
        var client = new StructuredOutputRepairingLlmClientDecorator(repairer)
            .Decorate(new EventClient(new LlmStreamEvent(Delta: "hello")));

        var events = await CollectAsync(client.StreamAsync(
            new LlmRequest([new LlmMessage("user", "hello")]),
            TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Which.Delta.Should().Be("hello");
    }

    [Fact]
    public async Task WithStructuredOutputRepair_DecoratesDirectClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmTools();
        using var provider = services.BuildServiceProvider();
        var client = new EventClient(
                new LlmStreamEvent(Delta: "{\"name\":\"Ada\""),
                new LlmStreamEvent(FinishReason: "stop"))
            .WithStructuredOutputRepair(
                provider.GetRequiredService<ILlmStructuredOutputRepairer>());
        var request = new LlmRequest(
            [new LlmMessage("user", "Return JSON")],
            responseFormat: LlmResponseFormat.JsonSchema(
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""));

        var response = await client.CompleteAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeTrue();
        using var document = JsonDocument.Parse(response.Content);
        document.RootElement.GetProperty("name").GetString().Should().Be("Ada");
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(
        IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var result = new List<LlmStreamEvent>();
        await foreach (var item in stream)
            result.Add(item);
        return result;
    }

    private sealed class EventClient(params LlmStreamEvent[] events) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in events)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class ThrowingRepairer : ILlmStructuredOutputRepairer
    {
        public Task<LlmResponse> RepairAsync(
            LlmResponse response,
            LlmResponseFormat responseFormat,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Repair should not run.");
    }
}
