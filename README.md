# Penghou.Baize

[![NuGet](https://img.shields.io/nuget/v/Penghou.Baize)](https://www.nuget.org/packages/Penghou.Baize)
[![CI](https://github.com/jenolaszlo-sketch/penghou-baize/actions/workflows/ci.yml/badge.svg)](https://github.com/jenolaszlo-sketch/penghou-baize/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/jenolaszlo-sketch/penghou-baize)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

Penghou.Baize is a provider-agnostic chat-completion client for .NET with a
single, stable programming model across OpenAI-compatible endpoints, Anthropic
Claude, Ollama, and Google Gemini. It exposes streaming, tool calling,
multimodal input, native batch execution, usage, and diagnostics through one
small domain surface — no provider SDK types leak into your application.

## Packages

| Package | Purpose |
| --- | --- |
| `Penghou.Baize` | Core domain: `ILlmClient`, `LlmRequest`, `LlmStreamEvent`, tool model |
| `Penghou.Baize.OpenAi` | OpenAI-compatible chat client (OpenAI, Azure, DeepSeek, ...) |
| `Penghou.Baize.Claude` | Anthropic Claude chat client |
| `Penghou.Baize.Ollama` | Ollama chat client |
| `Penghou.Baize.Gemini` | Google Gemini chat client |
| `Penghou.Baize.Router` | Provider-neutral, configuration-driven model routing and capability fallback |
| `Penghou.Baize.Batch` | Provider-neutral native batch planning and aggregate coordination |
| `Penghou.Baize.Tools` | Tool-call extraction, normalization, and result parsing |
| `Penghou.Baize.Extensions.AI` | `Microsoft.Extensions.AI.IChatClient` adapter |

The core, provider clients, and router target `net8.0` and `net10.0`.
`Penghou.Baize.Tools` targets `net9.0` and `net10.0` because its schema
generation uses the `System.Text.Json.Schema` APIs introduced in .NET 9.

## Install

```xml
<PackageReference Include="Penghou.Baize" Version="0.2.0" />
<!-- plus the client package for your provider(s) -->
<PackageReference Include="Penghou.Baize.OpenAi" Version="0.2.0" />
```

## Quick start

```csharp
using Penghou.Baize;
using Penghou.Baize.OpenAi;
using System.Runtime.CompilerServices;

ILlmClient client = new OpenAiChatClient(
    model: "gpt-4o",
    httpClientFactory: httpClientFactory,
    apiKey: apiKey,
    baseUrl: "https://api.openai.com/v1",
    capabilities: new LlmEndpointCapabilities
    {
        NativeToolCalling = true,
        ParallelToolCalls = true,
        NativeStructuredOutput = true,
        StructuredOutputViaTool = false,
        Thinking = true,
        ThinkingDisable = false,
        StreamingToolCallArguments = true
    });

var request = new LlmRequest(
    messages: [new LlmMessage("user", "Hello!")]);

await foreach (LlmStreamEvent e in client.StreamAsync(request))
{
    if (e.Delta is not null)
        Console.Write(e.Delta);
}
```

## Multimodal input

Messages can mix text, images, audio, video, and files. Media may be supplied
as immutable inline bytes, an absolute URI, or a provider-hosted file ID:

```csharp
var request = new LlmRequest(
[
    new LlmMessage(
        "user",
        [
            new LlmTextContent("Describe this diagram"),
            new LlmImageContent(
                "image/png",
                new LlmInlineDataSource(imageBytes))
        ])
]);
```

Capabilities declare both the accepted media types and their transports. The
router removes incompatible endpoints before ranking them, and the selected
provider validates the request again before transmission. Provider support is
intentionally conservative: configure only transports supported by the exact
model and API endpoint you use.

## Tool calling

Pass the tools the model may call. Tool-call deltas arrive as streaming
events; `Penghou.Baize.Router`'s `CompleteStreamingAsync` collects them into
`LlmResponse.ToolCalls`:

```csharp
var promptBuilder = new LlmPromptBuilder
{
    Messages = messages,
    Tools =
    [
        new LlmTool(
            "get_weather", "Returns the weather for a city",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")
    ]
};

var response = await router.CompleteStreamingAsync(
    ModelStrategy.ToolCall,
    promptBuilder);

foreach (var call in response.ToolCalls)
    Console.WriteLine($"{call.Name}: {call.ArgumentsJson}");
```

The deltas themselves (index, id/name on the first fragment, incremental
arguments) are still observable on the raw stream for progress UI:

```csharp
await foreach (var e in router.StreamAsync(
    ModelStrategy.ToolCall,
    promptBuilder))
{
    if (e.ToolCallDelta is { } delta)
        // accumulate by delta.Index; id/name arrive on the first fragment
}
```

### Structured output repair

Models also emit malformed JSON when asked for structured output. Pass the
response through `ILlmStructuredOutputRepairer` from `Penghou.Baize.Tools` to
repair it against the request's schema instead of retrying:

```csharp
services.AddLlmTools();
// ...
var repairer = provider.GetRequiredService<ILlmStructuredOutputRepairer>();

var response = await router.CompleteStreamingAsync(
    ModelStrategy.StructuredOutput,
    promptBuilder);

response = await repairer.RepairAsync(
    response,
    promptBuilder.ResponseFormat!);

foreach (var attempt in response.ContentRepairAttempts ?? [])
    Console.WriteLine($"{attempt.Name}: {attempt.Status}");
```

Repair strategies run against the whole content and each is reported as an
`LlmRepairAttempt` (scoped `content/...`), so callers can validate the result
and still fall back to a retry when repair could not produce schema-compliant
JSON.

`AddLlmTools` also accepts an `Action<JsonRepairOptions>` when an application
needs to add, remove, or reorder Nuwa repair strategies. Detailed shape,
tolerant-recovery, and winning-strategy diagnostics are available through
`ContentRepairDiagnostics` and `LlmToolCall.JsonRepairDiagnostics`. A repaired
document that still mismatches the supplied schema is reported but is not
applied to the response.

## Native batch inference

`Penghou.Baize.Batch` groups requests by configured endpoint and exposes the
provider's native asynchronous batch client without introducing an
orchestration-runtime dependency. OpenAI, Anthropic, and Gemini adapters support
native submission, polling, result retrieval, and cancellation according to
their advertised `BatchCapabilities`.

Register routing first, then batch planning:

```csharp
services.AddLlmRouting(configuration);
services.AddBaizeBatch(new BatchPlannerOptions
{
    MaxItemsPerGroup = 1_000
});

var batches = provider.GetRequiredService<IBaizeBatchCoordinator>();
var handle = await batches.SubmitAsync(new BaizeBatchSubmission(
[
    new BaizeBatchRequest(
        "request-1",
        new LlmRequest([new LlmMessage("user", "Summarize this")]),
        Model: "gpt-batch")
]));
var status = await batches.GetStatusAsync(handle);
var results = await batches.GetResultsAsync(handle);
```

Request IDs must be unique. Model names are preserved verbatim, including
colons; select a provider explicitly with `BaizeBatchRequest.CreateForProvider`
or the record's separate `Provider` property. Provider handles are validated so
they cannot accidentally be used with another provider adapter. Baize does not
currently provide durable workflow orchestration; applications should persist
the returned `ProviderBatchHandle` if polling must survive a process restart.

## Routing

`Penghou.Baize.Router` resolves a model name (or a `ModelStrategy`, with
fallback chains) to a concrete client from the `LlmRouting` configuration
section. The router package depends only on `Penghou.Baize`; it does not pull
OpenAI, Claude, Gemini, or Ollama into an application that does not use them.

Install the provider packages you need, then either register their adapters
explicitly or list their trusted assembly names under `ProviderModules`. The
configured module form lets the router discover a third-party
`ILlmClientProvider` without a Baize release or a growing provider enum:

```json
{
  "LlmRouting": {
    "ProviderModules": [
      { "Assembly": "Penghou.Baize.OpenAi" },
      { "Assembly": "Penghou.Baize.Claude" },
      { "Assembly": "Penghou.Baize.Ollama" }
    ],
    "Profiles": {
      "qwen-tools": {
        "NativeToolCalling": true,
        "ParallelToolCalls": true,
        "NativeStructuredOutput": true
      }
    },
    "Models": [
      {
        "Name": "deepseek",
        "Endpoints": [
          {
            "Provider": "OpenAi",
            "ProviderModel": "deepseek-chat",
            "BaseUrl": "https://api.deepseek.com/v1",
            "ApiKeyEnvVar": "DEEPSEEK_API_KEY",
            "Dialect": "DeepSeek"
          },
          {
            "Provider": "Claude",
            "ProviderModel": "claude-sonnet-4-5",
            "ApiKeyEnvVar": "ANTHROPIC_API_KEY"
          }
        ]
      },
      {
        "Name": "qwen",
        "Endpoints": [
          {
            "Provider": "Ollama",
            "ProviderModel": "qwen2.5-coder:7b",
            "BaseUrl": "http://localhost:11434",
            "Profile": "qwen-tools"
          }
        ]
      }
    ],
    "StrategyFallbacks": {
      "StructuredOutput": [ "deepseek", "qwen" ]
    }
  }
}
```

Every model needs a unique `Name` and at least one endpoint. `Provider` is a
case-insensitive adapter key; built-in keys are `OpenAi`, `Claude`, `Ollama`,
and `Gemini`, while packages can define their own. The older `ApiStyle`
property remains accepted for built-in providers. `BaseUrl` and
`ApiKeyEnvVar` override provider defaults. Provider-specific settings can be
placed in an endpoint's `Settings` object. For compatibility, OpenAI's
top-level `Dialect` (`Standard` or `DeepSeek`) and Claude's `ThinkingStyle`
are also forwarded as provider settings.

Assembly discovery accepts assembly identities, never file paths. The package
must be referenced by the application so it is present in the output and its
dependencies appear in the application's dependency manifest. Configuration
is a trust boundary: only list provider assemblies you intend to execute. If
an application is trimmed or compiled with Native AOT, prefer explicit
registration because reflection-based discovery cannot guarantee that
provider constructors are retained:

```csharp
services.AddOpenAiLlmProvider();
services.AddClaudeLlmProvider();
services.AddLlmRouting(configuration);
```

A third-party package implements `ILlmClientProvider`, exposes a public
DI-constructible implementation, and chooses a unique `LlmProviderKey`. A
module entry can set `Type` to its fully-qualified type name; when omitted,
Baize registers every public concrete provider in that assembly.

The router resolves each endpoint's capabilities in three layers, from the
most conservative to the most specific:

1. **Provider defaults** — only what the provider adapter guarantees. The
   OpenAI-compatible defaults claim tool definitions and streaming tool-call
   arguments but *not* parallel tool calls, native structured output, or
   extended thinking, because a generic "OpenAI-compatible" server does not
   guarantee `response_format` or reasoning effort. Claude and Gemini claim
   their documented native features. Ollama claims nothing beyond plain text
   streaming, because tool and JSON support depend on the local model, not the
   protocol.
2. **A named profile** (optional) — declared in the `Profiles` section and
   referenced from an endpoint through `Profile`. Profiles opt specific models
   into capabilities the conservative provider defaults do not claim, without
   duplicating them on every endpoint.
3. **Per-endpoint `Capabilities`** — override both the provider defaults and any
   referenced profile; an omitted capability inherits from the profile or the
   provider default.

Capabilities describe native tool calling, parallel tool calls, native or
tool-emulated structured output, extended thinking (and explicitly disabling
it), streaming tool-call arguments, accepted content types, and the reasoning
effort levels (`Low`, `Medium`, `High`, `Max`) the endpoint accepts when
thinking is enabled. A request asking for an effort level outside that set is
rejected rather than silently capped; `None` (no preference) is always
accepted. Providers that express thinking as a token budget (Gemini) can also
be given an explicit `ThinkingBudget` that overrides the effort-derived value,
so callers can match the model's documented range (for example 32768 for
Gemini 2.5 Pro) instead of relying on a hard-coded ceiling. When a request
asks for something the endpoint's capabilities do not allow, the client throws
a `LlmRequestValidationException` before transmitting instead of silently
dropping the feature. Clients are looked up either by name alone (the first
endpoint wins), by the extensible `(name, LlmProviderKey)` pair, or through
the legacy `(name, ApiStyle)` overload for built-in providers:

```csharp
var services = new ServiceCollection();
services.AddHttpClient();
services.AddLlmRouting(configuration); // reads the "LlmRouting" section

await using var provider = services.BuildServiceProvider();
var lookup = provider.GetRequiredService<ILlmModelLookup>();

ILlmClient byName = lookup.GetClient("deepseek");
ILlmClient byNameAndStyle = lookup.GetClient("deepseek", ApiStyle.Claude);
ILlmClient byProvider = lookup.GetClient("deepseek", new LlmProviderKey("Claude"));

var router = provider.GetRequiredService<ILlmRouter>();

await foreach (var e in router.StreamAsync(
    ModelStrategy.StructuredOutput,
    new LlmPromptBuilder { Messages = [new LlmMessage("user", "Hello")] }))
{
    if (e.Delta is not null)
        Console.Write(e.Delta);
}
```

### Same-call fallback

`StreamAsync` retries the request against the next endpoint in the fallback
chain when an attempt fails before producing meaningful output (content or
tool-call deltas). Once content or tool-call deltas have been streamed, the
router does not reissue the request - reissuing would duplicate output or
repeat side effects - and the failure surfaces to the caller instead. All
attempts share a single per-request `RequestTimeout` deadline, so a slow
first attempt does not multiply the budget. Every stream ends with a
`LlmRouterDiagnostics` event (also surfaced as
`LlmResponse.RouterDiagnostics`) describing each attempt, its endpoint,
outcome, and duration:

```csharp
var response = await router.CompleteStreamingAsync(
    ModelStrategy.StructuredOutput,
    new LlmPromptBuilder { Messages = [new LlmMessage("user", "Hello")] });

foreach (var attempt in response.RouterDiagnostics?.Attempts ?? [])
    Console.WriteLine(
        $"{attempt.EndpointId}: {attempt.Outcome}");
```

### Router memory

The router keeps per-endpoint history so it can prefer the least-failing
endpoint instead of blindly taking the first registered one:

- **Calls** are recorded automatically for each endpoint attempt, and
  **availability failures** (connection errors, timeouts, HTTP 5xx) are
  recorded when an attempt fails - the same request is then retried against
  the next endpoint unless meaningful output had already been streamed.
- **Quality failures** - a tool call that needed repair, or structured
  output that didn't match the requested schema - are reported by the
  application after the fact through `ILlmRouterMemory`:

```csharp
var memory = provider.GetRequiredService<ILlmRouterMemory>();
var endpoint = router.Resolve(ModelStrategy.StructuredOutput);

await memory.RecordCallAsync(endpoint.EndpointId);
await memory.RecordFailureAsync(
    endpoint.EndpointId,
    LlmFailureCategory.StructuredOutputMismatch);
```

`Resolve` returns the `ResolvedEndpoint` the router would currently pick - its
stable `EndpointId`, the logical model name, and provider key - so applications
know where a stream is going and can attribute quality events correctly.
Routing memory and cooldowns are keyed by endpoint id, so two endpoints of the
same logical model keep separate stats (give them distinct `Id` values in
configuration; an explicit id also keeps stats stable across renames or
reordering).

Availability failures are counted within a sliding window; call counts and
feature-reliability failures are cumulative. The package ships an in-memory
implementation (`InMemoryLlmRouterMemory`). To use durable or shared storage
(Redis, a database, ...), register your own `ILlmRouterMemory` on the service
collection after `AddLlmRouting`:

```csharp
services.AddLlmRouting(configuration);
services.AddSingleton<ILlmRouterMemory, MyRedisRouterMemory>();
```

See the `LlmRouting` configuration shape in
`src/Penghou.Baize.Router/Configuration/LlmRoutingOptions.cs`.

### Custom endpoint selection

Hard capability filtering always runs first. Applications can replace the
default reliability ranking after `AddLlmRouting` to apply cost, latency,
region, tenant, or workload-specific policy without forking the router:

```csharp
services.AddLlmRouting(configuration);
services.AddSingleton<ILlmEndpointSelectionPolicy, MySelectionPolicy>();
```

## Microsoft.Extensions.AI

`Penghou.Baize.Extensions.AI` adapts any `ILlmClient` to `IChatClient`, so
Baize clients work with the standard .NET AI middleware and ecosystem:

```csharp
using Microsoft.Extensions.AI;
using Penghou.Baize.Extensions.AI;

IChatClient chatClient = new BaizeChatClient(client, "OpenAi", "gpt-4o");
var response = await chatClient.GetResponseAsync("Hello");
```

Text, usage, reasoning, tool calls/results, and supported multimodal content
are mapped in both directions.

## Observability

Clients and routed attempts emit `Activity` spans and request, failure,
latency, and token metrics from the `Penghou.Baize` instrumentation source.
Register that source with OpenTelemetry in the host application:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(BaizeTelemetry.InstrumentationName))
    .WithMetrics(metrics => metrics.AddMeter(BaizeTelemetry.InstrumentationName));
```

## License

MIT
