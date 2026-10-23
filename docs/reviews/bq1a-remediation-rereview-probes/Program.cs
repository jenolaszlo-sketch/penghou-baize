using System.Net;
using System.Text;
using System.Text.Json;
using Penghou.Baize;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Qualification;
using Penghou.Baize.Qualification.Tests;

const string toolArguments = "{\"value\":\"baize-live\"}";
LlmStreamEvent Tool(string? id = "call_1", string json = toolArguments) => new(ToolCallDelta: new(0, id, "echo_value", json));
LlmStreamEvent Text() => new(Delta: "BAIZE_OK");
LlmStreamEvent Stop() => new(FinishReason: "stop");
LlmStreamEvent Done(bool? done) => new(Diagnostics: new("fixture", Done: done));
LlmStreamEvent Router(params LlmRouterAttemptOutcome[] outcomes) => new(RouterDiagnostics: new(outcomes.Select(o => new LlmRouterAttempt("fixture", "fixture", "fixture", o, TimeSpan.Zero)).ToArray()));
void Print(string name, string expectation, QualificationResultV3 result, int calls) => Console.WriteLine(JsonSerializer.Serialize(new
{
    name, expectation, result.Succeeded, calls,
    contracts = result.Contracts.Select(c => new { c.ContractId, disposition = c.Disposition.ToString(), checks = c.Assertions.Select(a => new { a.Id, status = a.Status.ToString(), a.Code }) }),
    identity = new { protocol = result.Identity.Protocol.ToString(), canonical = result.Identity.CanonicalCorrelation.ToString(), origin = result.Identity.CanonicalOrigin.ToString(), applicability = result.Identity.ProviderIdentityApplicability.ToString(), provider = result.Identity.ProviderIdentityStatus.ToString(), result.Identity.ProviderIdentityCode },
    result.Router, result.Diagnostics
}));
async Task Probe(string name, string expected, ScenarioFixture fixture, bool tools = false, bool router = false,
    ToolIdentityProtocol protocol = ToolIdentityProtocol.OpenAiChatCompletions, CancellationToken caller = default, CancellationToken deadline = default)
{
    var context = new QualificationContextV3(fixture.Context().Stream, tools, router, IdentityProtocol: protocol, Deadline: deadline);
    var result = tools ? await QualificationScenariosV3.RunSingleToolAsync(context, caller) : await QualificationScenariosV3.RunBaselineAsync(context, caller);
    Print(name, expected, result, fixture.Calls);
}

await Probe("control-baseline", "pass", new(Text(), Stop()));
await Probe("control-tool", "pass", new(Tool(), Stop()), tools: true);
await Probe("F2-undeclared-tool", "reject", new(Text(), Tool(), Stop()));
await Probe("F2-answer-with-tool", "reject", new(new LlmStreamEvent(Delta: "I refuse to call the tool."), Tool(), Stop()), tools: true);
await Probe("F3-zero-router", "reject", new(Text(), Stop(), Router()), router: true);
await Probe("F3-failed-router", "reject", new(Text(), Stop(), Router(LlmRouterAttemptOutcome.Failed)), router: true);
await Probe("F3-replaced-router-failure", "reject-and-retain-earlier-failure", new(Text(), Stop(), Router(LlmRouterAttemptOutcome.Failed), Router(LlmRouterAttemptOutcome.Succeeded)), router: true);
await Probe("F3-premature-router-success", "reject", new(Router(LlmRouterAttemptOutcome.Succeeded), Text(), Stop()), router: true);
await Probe("F3-valid-router", "pass", new(Text(), Stop(), Router(LlmRouterAttemptOutcome.Succeeded)), router: true);
await Probe("F4-foreign-cancellation", "inconclusive-ambiguous", new() { Error = new OperationCanceledException(new CancellationToken(true)) });
using (var deadline = new CancellationTokenSource())
    await Probe("F4-known-deadline", "blocked-timeout", new(Text()) { BeforeCompletion = deadline.Cancel }, deadline: deadline.Token);
using (var caller = new CancellationTokenSource())
    await Probe("F4-caller-cancellation", "cancelled", new() { BeforeCompletion = caller.Cancel }, caller: caller.Token);
await Probe("no-output-invalid-request", "blocked-and-not-observed-content", new() { Error = new LlmClientException("fixture", 400) });
await Probe("duplicate-escaped-property", "reject-with-value-not-observed", new(Tool(json: "{\"value\":\"baize-live\",\"\\u0076alue\":\"wrong\"}"), Stop()), tools: true);
await Probe("malformed-json", "reject-with-uniqueness-not-observed", new(Tool(json: "{"), Stop()), tools: true);
await Probe("repair-detail-only", "reject-native-repair-path", new(Tool() with { ContentRepairDiagnostics = new(LlmRepairShapeStatus.Matched, [], "repair") { IsRepairAccepted = true } }, Stop()), tools: true);
await Probe("repair-flag-cannot-be-cleared", "reject-native-repair-path", new(Tool() with { ContentWasRepaired = true }, new LlmStreamEvent { ContentWasRepaired = false }, Stop()), tools: true);
await Probe("opaque-long-id", "pass-correlation-no-provenance", new(Tool(new string('x', 257)), Stop()), tools: true);
await Probe("required-protocol-no-id", "reject", new(Tool(null), Stop()), tools: true);
await Probe("optional-protocol-no-id", "pass-correlation-provenance-unknown", new(Tool(null), Stop()), tools: true, protocol: ToolIdentityProtocol.GeminiGenerateContent);
await Probe("public-coterminal-event", "pass", new(Tool() with { FinishReason = "tool_calls" }), tools: true);

await Probe("R1-explicit-incomplete-control", "reject", new(Text(), Stop(), Done(false)));
await Probe("R1-incomplete-then-unknown", "must-not-pass-after-incomplete-terminal", new(Text(), Stop(), Done(false), Done(null)));
await Probe("R1-tool-incomplete-then-unknown", "must-not-pass-after-incomplete-terminal", new(Tool(), Stop(), Done(false), Done(null)), tools: true);
await Probe("R1-routed-incomplete-then-unknown", "must-not-pass-after-incomplete-terminal", new(Text(), Stop(), Done(false), Done(null), Router(LlmRouterAttemptOutcome.Succeeded)), router: true);
await Probe("R1-intermediate-false-then-complete", "pass-normal-completion-transition", new(Done(false), Text(), Stop(), Done(true)));
await Probe("R1-complete-then-metadata", "pass", new(Text(), Stop(), Done(true), Done(null)));

const string openAiTool = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"echo_value\",\"arguments\":\"{\\\"value\\\":\\\"baize-live\\\"}\"}}]},\"finish_reason\":{FINISH}}]}\n\n";
foreach (var terminal in new[] { false, true })
{
    var body = openAiTool.Replace("{FINISH}", terminal ? "\"tool_calls\"" : "null") +
        (terminal ? "" : "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n") + "data: [DONE]\n\n";
    using var factory = new FakeHttpFactory(body);
    var client = new OpenAiChatClient("fixture", factory, "FAKE", "https://fixture.invalid/v1", new() { NativeToolCalling = true });
    Print(terminal ? "F1-real-openai-coterminal" : "real-openai-split-control", terminal ? "reject-known-adapter-defect" : "pass",
        await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true, IdentityProtocol: ToolIdentityProtocol.OpenAiChatCompletions)), factory.Calls);
}
foreach (var suppliedId in new[] { false, true })
{
    var body = "{\"model\":\"fixture\",\"message\":{\"role\":\"assistant\",\"tool_calls\":[{" + (suppliedId ? "\"id\":\"provider_call_17\"," : "") +
        "\"function\":{\"index\":0,\"name\":\"echo_value\",\"arguments\":{\"value\":\"baize-live\"}}}]},\"done\":false}\n" +
        "{\"model\":\"fixture\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\"}\n";
    using var factory = new FakeHttpFactory(body);
    var client = new OllamaChatClient("fixture", factory, "", "https://fixture.invalid", new() { NativeToolCalling = true });
    Print(suppliedId ? "R2-real-ollama-wire-id-present" : "real-ollama-no-wire-id", suppliedId ? "native-valid-but-wire-provenance-not-inapplicable" : "native-valid-no-wire-id-required",
        await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true, IdentityProtocol: ToolIdentityProtocol.OllamaChat)), factory.Calls);
}
foreach (var suppliedId in new[] { false, true })
{
    var body = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{" + (suppliedId ? "\"id\":\"provider_call_17\"," : "") +
        "\"name\":\"echo_value\",\"args\":{\"value\":\"baize-live\"}}}]},\"finishReason\":\"STOP\"}]}\n\ndata: [DONE]\n\n";
    using var factory = new FakeHttpFactory(body);
    var client = new GeminiChatClient("fixture", factory, "FAKE", "https://fixture.invalid", new() { NativeToolCalling = true });
    Print(suppliedId ? "real-gemini-wire-id-present" : "real-gemini-no-wire-id", "pass-correlation-provenance-not-observed",
        await QualificationScenariosV3.RunSingleToolAsync(new(client.StreamAsync, true, IdentityProtocol: ToolIdentityProtocol.GeminiGenerateContent)), factory.Calls);
}

sealed class FakeHttpFactory(string body) : IHttpClientFactory, IDisposable
{
    private readonly Handler handler = new(body);
    public int Calls => handler.Calls;
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    public void Dispose() => handler.Dispose();
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });
        }
    }
}
