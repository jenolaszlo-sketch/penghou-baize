using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Penghou.Baize;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Ollama;
using Penghou.Baize.Qualification;
using Penghou.Baize.Qualification.Tests;

const string toolArguments = "{\"value\":\"baize-live\"}";
LlmStreamEvent Call(string json = toolArguments, string id = "call_1") => new(ToolCallDelta: new(0, id, "echo_value", json));
LlmStreamEvent Stop() => new(FinishReason: "stop");
async Task Probe(string name, ScenarioFixture fixture, bool tools = false, bool router = false)
{
    var result = tools
        ? await QualificationScenarios.RunSingleToolAsync(fixture.Context(true) with { RequireRouterDiagnostics = router })
        : await QualificationScenarios.RunBaselineAsync(fixture.Context() with { RequireRouterDiagnostics = router });
    Print(name, result);
}
void Print(string name, QualificationResult result) => Console.WriteLine(JsonSerializer.Serialize(new
{
    name, result.Succeeded,
    contracts = result.Contracts.Select(c => new { c.ContractId, disposition = c.Disposition.ToString(), checks = c.Assertions.Select(a => new { a.Id, status = a.Status.ToString(), a.Code }) }),
    result.Diagnostics
}));

await Probe("good-baseline", new(new LlmStreamEvent(Delta: "BAIZE_OK"), Stop()));
await Probe("good-tool", new(Call(), Stop()), true);
await Probe("tool-with-unrequested-answer", new(new LlmStreamEvent(Delta: "I refuse to call the tool."), Call(), Stop()), true);
await Probe("baseline-with-undeclared-tool", new(new LlmStreamEvent(Delta: "BAIZE_OK"), Call(), Stop()));
await Probe("duplicate-conflicting-value", new(Call("{\"value\":\"WRONG\",\"value\":\"baize-live\"}"), Stop()), true);
await Probe("foreign-cancellation", new() { Error = new OperationCanceledException(new CancellationToken(true)) });
await Probe("generic-provider-400", new() { Error = new LlmClientException("private", LlmClientFailureKind.InvalidRequest, 400) });
await Probe("untyped-execution-error", new() { Error = new InvalidOperationException("private") });
await Probe("zero-router-attempts", new(new LlmStreamEvent(Delta: "BAIZE_OK"), Stop(), new(RouterDiagnostics: new([]))), router: true);
await Probe("failed-router-attempt", new(new LlmStreamEvent(Delta: "BAIZE_OK"), Stop(), new(RouterDiagnostics: new([new("fixture", "fixture", "OpenAi", LlmRouterAttemptOutcome.Failed, TimeSpan.Zero)]))), router: true);
await Probe("repair-diagnostics-only", new(Call() with { ContentRepairDiagnostics = new(LlmRepairShapeStatus.Matched, [], "repair") { IsRepairAccepted = true } }, Stop()), true);
await Probe("opaque-id-over-256", new(Call(id: new string('x', 257)), Stop()), true);
await Probe("late-tool-at-same-terminal", new(Call() with { FinishReason = "tool_calls" }), true);
await Probe("different-part-index-for-tool-fragments", new(new LlmStreamEvent(ToolCallDelta: new(0, "call_1", "echo_value", "{\"value\":")) { PartIndex = 0 }, new(ToolCallDelta: new(0, ArgumentsJsonFragment: "\"baize-live\"}")) { PartIndex = 1 }, Stop()), true);

const string coTerminal = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"echo_value\",\"arguments\":\"{\\\"value\\\":\\\"baize-live\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\ndata: [DONE]\n\n";
using (var factory = new FakeHttpFactory(coTerminal))
{
    var client = new OpenAiChatClient("fixture", factory, "FAKE", "https://fixture.invalid/v1", new() { NativeToolCalling = true });
    Print("real-openai-coterminal", await QualificationScenarios.RunSingleToolAsync(new(client.StreamAsync, true)));
}
const string ollamaBody = "{\"model\":\"fixture\",\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"echo_value\",\"arguments\":{\"value\":\"baize-live\"}}}]},\"done\":false}\n{\"model\":\"fixture\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\"}\n";
using (var factory = new FakeHttpFactory(ollamaBody))
{
    var client = new OllamaChatClient("fixture", factory, "", "https://fixture.invalid", new() { NativeToolCalling = true });
    Print("real-ollama-native-tool-without-id", await QualificationScenarios.RunSingleToolAsync(new(client.StreamAsync, true)));
}

sealed class FakeHttpFactory(string body) : IHttpClientFactory, IDisposable
{
    private readonly Handler handler = new(body);
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    public void Dispose() => handler.Dispose();
    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });
    }
}
