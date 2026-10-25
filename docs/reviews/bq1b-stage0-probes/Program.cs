using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.Claude;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Qualification;
using Penghou.Baize.Router;
using Penghou.Http.Abstractions;
using Penghou.Model.Abstractions;

const string openAi = "data: {\"choices\":[{\"delta\":{\"content\":\"BAIZE_OK\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
const string claude = """
    event: message_start
    data: {"type":"message_start","message":{"id":"fixture","model":"fixture-model","usage":{"input_tokens":3,"output_tokens":0}}}

    event: content_block_start
    data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

    event: content_block_delta
    data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"BAIZE_OK"}}

    event: content_block_stop
    data: {"type":"content_block_stop","index":0}

    event: message_delta
    data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":2}}

    event: message_stop
    data: {"type":"message_stop"}


    """;
int records = 0;
void Record(string name, bool invariant, object evidence)
{
    Console.WriteLine(JsonSerializer.Serialize(new { name, invariant, evidence, physicalWireAttempts = (int?)null }));
    if (!invariant) throw new InvalidOperationException("Probe invariant failed: " + name);
    records++;
}
HttpResponseMessage Reply(string body = openAi, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
Terminal Backend(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>>? reply = null) => new(reply ?? ((_, _, _) => Task.FromResult(Reply())));
LlmRequest Request() => new([new LlmMessage("user", "fixture")], maxTokens: 512);
async Task<LlmResponse> Collect(IAsyncEnumerable<LlmStreamEvent> stream) => await stream.CollectAsync();
async Task<Exception?> Error(Func<Task> action)
{
    try { await action().WaitAsync(TimeSpan.FromSeconds(3)); return null; }
    catch (Exception ex) { return ex; }
}

// Actual native adapters, fake HTTP only. No model validity inference is made.
foreach (var (provider, body) in new (ILlmClientProvider, string)[]
{
    (new OpenAiClientProvider(), openAi),
    (new ClaudeClientProvider(), claude),
    (new GeminiClientProvider(), "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"BAIZE_OK\"}]},\"finishReason\":\"STOP\"}]}\n\ndata: [DONE]\n\n"),
    (new OllamaClientProvider(), "{\"model\":\"fixture-model\",\"message\":{\"role\":\"assistant\",\"content\":\"BAIZE_OK\"},\"done\":true,\"done_reason\":\"stop\"}\n")
})
{
    var terminal = Backend((_, _, _) => Task.FromResult(Reply(body)));
    using var factory = new Factory(terminal);
    var http = new NeutralProbe(new BaizeHttpTransport(factory));
    var model = new ModelProbe();
    var client = (LlmClientBase)provider.CreateClient(new("fixture-model", BaizeHttp.CreateClientFactory(http), "FAKE",
        "https://fixture.invalid/v1", new(), new Dictionary<string, string>()));
    client.ModelTransportFactory = model;
    var result = await QualificationScenariosV3.RunBaselineAsync(new(client.StreamAsync));
    var serializedOutputCap = terminal.OutputCaps.Single();
    Record("native-baseline-" + provider.Key.Value, result.Succeeded && model.Calls == 1 && http.Calls == 1 && terminal.Calls == 1 && http.NullContexts == 1 && http.ActiveScopes == 1 && serializedOutputCap == 512,
        new { result.Succeeded, semanticEntries = model.Calls, neutralSends = http.Calls, backendSends = terminal.Calls, http.NullContexts, http.ActiveScopes, serializedOutputCap });
}

{
    var terminal = Backend(); using var factory = new Factory(terminal);
    await using var fixture = new RouteFixture(factory, new(0));
    var error = await Error(() => Collect(fixture.Router.StreamAsync("fixture", Request())));
    Record("semantic-denial-before-secrets", error is ModelAccessDeniedException && fixture.Secrets.Reads == 0 && terminal.Calls == 0,
        new { semanticEntries = fixture.Model.Calls, secretReads = fixture.Secrets.Reads, backendSends = terminal.Calls });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal); var provider = new ProbeProvider();
    await using var fixture = new RouteFixture(factory, new(0), provider: provider);
    var endpoints = fixture.Services.GetRequiredService<ILlmModelLookup>().GetEndpoints("fixture");
    Record("metadata-resolution-is-credential-free", endpoints.Count == 1 && fixture.Model.Calls == 0 && fixture.Secrets.Reads == 0 && provider.Constructions == 0 && terminal.Calls == 0,
        new { endpointCount = endpoints.Count, semanticEntries = fixture.Model.Calls, secretReads = fixture.Secrets.Reads, provider.Constructions, backendSends = terminal.Calls });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal); var provider = new ProbeProvider();
    await using var fixture = new RouteFixture(factory, new(0), provider: provider);
    var report = await fixture.Services.GetRequiredService<ILlmEndpointValidator>().ValidateAsync();
    Record("endpoint-validation-bypasses-semantic-admission-for-construction", report.Succeeded && fixture.Model.Calls == 0 && fixture.Secrets.Reads == 1 && provider.Constructions == 1 && terminal.Calls == 0,
        new { report.Succeeded, semanticEntries = fixture.Model.Calls, secretReads = fixture.Secrets.Reads, provider.Constructions, backendSends = terminal.Calls });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal);
    await using var fixture = new RouteFixture(factory, new(1));
    var stream = fixture.Router.StreamAsync("fixture", Request());
    var beforeEnumeration = fixture.Model.Calls;
    await Collect(stream);
    var error = await Error(() => Collect(stream));
    Record("one-admission-per-enumeration", beforeEnumeration == 0 && error is ModelAccessDeniedException && fixture.Model.Calls == 2 && terminal.Calls == 1,
        new { beforeEnumeration, semanticEntries = fixture.Model.Calls, secretReads = fixture.Secrets.Reads, neutralSends = fixture.Http.Calls, backendSends = terminal.Calls, fixture.Http.NullContexts, fixture.Http.ActiveScopes });
}
{
    var terminal = Backend((request, _, _) => Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/0/", StringComparison.Ordinal)
        ? Reply("fixture", HttpStatusCode.ServiceUnavailable) : Reply()));
    using var factory = new Factory(terminal);
    await using var fixture = new RouteFixture(factory, endpoints: 2);
    var response = await Collect(fixture.Router.StreamAsync("fixture", Request()));
    Record("maximum-attempts-one-still-allows-endpoint-fallback", response.Content == "BAIZE_OK" && terminal.Calls == 2 && fixture.Model.Calls == 2,
        new { semanticEntries = fixture.Model.Calls, secretReads = fixture.Secrets.Reads, backendSends = terminal.Calls, routerAttempts = response.RouterDiagnostics?.Attempts.Count });
}
foreach (var guarded in new[] { false, true })
{
    var terminal = Backend((_, call, _) => Task.FromResult(call == 1 ? Reply("fixture", HttpStatusCode.ServiceUnavailable) : Reply()));
    var guard = new LastGuard(terminal);
    using var factory = new Factory(new RetryOnce(guarded ? guard : terminal));
    await using var fixture = new RouteFixture(factory);
    var error = await Error(() => Collect(fixture.Router.StreamAsync("fixture", Request())));
    Record(guarded ? "innermost-guard-denies-handler-retry" : "neutral-count-misses-handler-retry",
        fixture.Http.Calls == 1 && terminal.Calls == (guarded ? 1 : 2) && (guarded ? error is HttpRequestException : error is null),
        new { semanticEntries = fixture.Model.Calls, neutralSends = fixture.Http.Calls, finalGuardEntries = guarded ? guard.Entries : (int?)null, backendSends = terminal.Calls });
}
foreach (var guarded in new[] { false, true })
{
    var terminal = Backend();
    using var factory = new Factory(new Retarget(guarded ? new LastGuard(terminal, bindHost: true) : terminal));
    await using var fixture = new RouteFixture(factory);
    var error = await Error(() => Collect(fixture.Router.StreamAsync("fixture", Request())));
    Record(guarded ? "final-destination-check-stops-substitution" : "inner-handler-can-substitute-destination",
        guarded ? error is HttpRequestException && terminal.Calls == 0 : error is null && terminal.OtherHosts == 1,
        new { neutralSends = fixture.Http.Calls, backendSends = terminal.Calls, terminal.OtherHosts });
}
{
    var terminal = Backend(); var guard = new LastGuard(terminal);
    using var factory = new Factory(guard);
    var transport = new BaizeHttpTransport(factory);
    var ready = 0;
    var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var attempts = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
    {
        if (Interlocked.Increment(ref ready) == 2) allReady.SetResult();
        await start.Task;
        try { await using var response = await transport.SendAsync(new(i.ToString(), "GET", new("https://fixture.invalid/"))); return true; }
        catch (HttpRequestException) { return false; }
    })).ToArray();
    await allReady.Task.WaitAsync(TimeSpan.FromSeconds(2));
    start.SetResult();
    var results = await Task.WhenAll(attempts);
    Record("atomic-final-slot-fixture", results.Count(r => r) == 1 && guard.Entries == 2 && terminal.Calls == 1,
        new { participantsReady = ready, admitted = results.Count(r => r), guard.Entries, backendSends = terminal.Calls });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal);
    var http = new NeutralProbe(new BaizeHttpTransport(factory));
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://fixture.invalid/") { Content = new StringContent("abcdef") };
    var error = await Error(async () => { using var response = await BaizeHttpTransportAdapter.SendAsync(http, request, new(maximumRequestBodyBytes: 2), null, default); });
    Record("legacy-request-byte-limit-before-neutral-send", error is InvalidDataException && http.Calls == 0 && terminal.Calls == 0,
        new { neutralSends = http.Calls, backendSends = terminal.Calls });
}
{
    var body = new ControlledStream(Encoding.UTF8.GetBytes("abcdef"));
    var terminal = Backend((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
    using var factory = new Factory(terminal);
    await using var response = await new BaizeHttpTransport(factory).SendAsync(new("bytes", "GET", new("https://fixture.invalid/"), limits: new(maximumResponseBodyBytes: 3)));
    var error = await Error(async () => { _ = await response.Body.ReadAsync(4); });
    Record("response-limit-is-delivery-bound-not-wire-byte-bound", error is InvalidDataException && body.ReadBytes == 4 && body.Disposed,
        new { configuredBodyBytes = 3, body.ReadBytes, body.Disposed, backendSends = terminal.Calls });
}
foreach (var deadline in new[] { false, true })
{
    var body = new ControlledStream(blockReads: true);
    var terminal = Backend((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
    using var factory = new Factory(terminal);
    await using var response = await new BaizeHttpTransport(factory).SendAsync(new("cancel", "GET", new("https://fixture.invalid/"),
        limits: new(timeout: deadline ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5))));
    using var idle = new CancellationTokenSource();
    var read = response.Body.ReadAsync(8, idle.Token).AsTask();
    await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    if (!deadline) idle.Cancel();
    var error = await Error(async () => { _ = await read; });
    await response.DisposeAsync();
    Record(deadline ? "existing-total-timeout-reaches-body-read" : "body-read-port-supports-idle-token",
        error is OperationCanceledException && body.Disposed, new { callerCancelled = idle.IsCancellationRequested, body.Disposed, backendSends = terminal.Calls });
}
{
    var body = new ControlledStream(blockDispose: true);
    var terminal = Backend((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
    using var factory = new Factory(terminal);
    var response = await new BaizeHttpTransport(factory).SendAsync(new("dispose", "GET", new("https://fixture.invalid/")));
    var disposal = Task.Run(async () => await response.DisposeAsync());
    await body.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var waitedForRelease = !disposal.IsCompleted;
    body.ReleaseDispose.SetResult();
    await disposal.WaitAsync(TimeSpan.FromSeconds(2));
    Record("dispose-async-can-block-synchronously", waitedForRelease && body.Disposed, new { waitedForRelease, body.Disposed });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal);
    var secrets = new Secrets(delayed: true); var provider = new ProbeProvider();
    await using var fixture = new RouteFixture(factory, secrets: secrets, provider: provider);
    using var caller = new CancellationTokenSource();
    var collection = Collect(fixture.Router.StreamAsync("fixture", Request(), caller.Token));
    await secrets.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    caller.Cancel();
    var error = await Error(async () => { _ = await collection; });
    secrets.Release.SetResult("FAKE");
    await provider.Created.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Record("deferred-construction-outlives-cancelled-waiter", error is OperationCanceledException && !secrets.TokenCanBeCancelled && provider.Constructions == 1 && terminal.Calls == 0,
        new { secrets.TokenCanBeCancelled, provider.Constructions, backendSends = terminal.Calls, closedLease = fixture.Model.Leases.All(l => l.Closed) });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal); var model = new ModelProbe();
    await using var fixture = new RouteFixture(factory, model, provider: new ProbeProvider(model));
    await Collect(fixture.Router.StreamAsync("fixture", Request()));
    Record("double-installing-model-factory-double-counts", model.Calls == 2 && terminal.Calls == 1,
        new { semanticPortEntries = model.Calls, backendSends = terminal.Calls });
}
foreach (var frozen in new[] { false, true })
{
    var terminal = Backend(); using var factory = new Factory(terminal);
    var model = new ModelProbe { ExpectedModel = frozen ? "fixture-model" : null };
    await using var fixture = new RouteFixture(factory, model);
    await Collect(fixture.Router.StreamAsync("fixture", Request()));
    fixture.Config["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = "changed-fixture-model";
    fixture.Config.Reload();
    var error = await Error(() => Collect(fixture.Router.StreamAsync("fixture", Request())));
    Record(frozen ? "semantic-binding-check-denies-reload-drift" : "default-router-allows-configuration-reload",
        frozen ? error is ModelAccessDeniedException && terminal.Calls == 1 : error is null && terminal.Models.Distinct().Count() == 2,
        new { semanticEntries = model.Calls, backendSends = terminal.Calls, distinctWireModels = terminal.Models.Distinct().Count() });
}
{
    var terminal = Backend(); using var factory = new Factory(terminal);
    await using var fixture = new RouteFixture(factory);
    fixture.Http.RequireActiveLease = true;
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Task<Exception?>? background = null;
    fixture.Http.OnSend = () =>
    {
        fixture.Http.OnSend = null;
        background = Task.Run(async () =>
        {
            await release.Task;
            return await Error(async () => { await using var response = await fixture.Http.SendAsync(new("late", "GET", new("https://fixture.invalid/"))); });
        });
    };
    await Collect(fixture.Router.StreamAsync("fixture", Request()));
    release.SetResult();
    var error = await background!.WaitAsync(TimeSpan.FromSeconds(2));
    Record("captured-background-scope-can-be-revoked", error is InvalidOperationException && terminal.Calls == 1 && fixture.Model.Leases.All(l => l.Closed),
        new { neutralEntries = fixture.Http.Calls, backendSends = terminal.Calls, closedLease = fixture.Model.Leases.All(l => l.Closed) });
}
{
    const string partial = "{\"model\":\"fixture-model\",\"message\":{\"role\":\"assistant\",\"content\":\"BAIZE_OK\"},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":3}\n";
    var terminal = Backend((_, _, _) => Task.FromResult(Reply(partial))); using var factory = new Factory(terminal);
    var client = new OllamaChatClient("fixture-model", factory, "", "https://fixture.invalid/", new());
    var response = await Collect(client.StreamAsync(Request()));
    Record("ollama-partial-usage-has-derived-total", response.Usage is { PromptTokens: 3, CompletionTokens: null, TotalTokens: 3, Cost: null },
        new { response.Usage, completeBillableUsageEstablished = false });
}
{
    const string reasoning = "data: {\"choices\":[{\"delta\":{\"content\":\"BAIZE_OK\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":5,\"completion_tokens_details\":{\"reasoning_tokens\":1}}}\n\ndata: [DONE]\n\n";
    var terminal = Backend((_, _, _) => Task.FromResult(Reply(reasoning))); using var factory = new Factory(terminal);
    var client = new OpenAiChatClient("fixture-model", factory, "FAKE", "https://fixture.invalid/", new());
    var response = await Collect(client.StreamAsync(Request()));
    Record("openai-wire-reasoning-detail-not-exposed", response.Usage is { PromptTokens: 3, CompletionTokens: 2, ThinkingTokens: null, Cost: null },
        new { response.Usage, completeBillableUsageEstablished = false });
}
// Harness completion goes to stderr so stdout remains a deterministic JSONL set.
Console.Error.WriteLine($"Completed {records} Stage 0 boundary probes; no sockets or real provider services invoked.");
