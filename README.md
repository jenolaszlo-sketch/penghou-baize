# Penghou.Baize

[![NuGet](https://img.shields.io/nuget/v/Penghou.Baize)](https://www.nuget.org/packages/Penghou.Baize)
[![CI](https://github.com/jenolaszlo-sketch/penghou-baize/actions/workflows/ci.yml/badge.svg)](https://github.com/jenolaszlo-sketch/penghou-baize/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/jenolaszlo-sketch/penghou-baize)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

Penghou.Baize is a provider-agnostic chat-completion client for .NET with a
single, stable programming model across OpenAI-compatible endpoints, Anthropic
Claude, Ollama, and Google Gemini. It exposes streaming, tool calling, usage,
and diagnostics through one small domain surface — no provider SDK types leak
into your application.

## Packages

| Package | Purpose |
| --- | --- |
| `Penghou.Baize` | Core domain: `ILlmClient`, `LlmRequest`, `LlmStreamEvent`, tool model |
| `Penghou.Baize.OpenAi` | OpenAI-compatible chat client (OpenAI, Azure, DeepSeek, ...) |
| `Penghou.Baize.Claude` | Anthropic Claude chat client |
| `Penghou.Baize.Ollama` | Ollama chat client |
| `Penghou.Baize.Gemini` | Google Gemini chat client |
| `Penghou.Baize.Router` | Configuration-driven model routing and capability fallback |
| `Penghou.Baize.Tools` | Tool-call extraction, normalization, and result parsing |

The core, provider clients, and router target `net8.0` and `net10.0`.
`Penghou.Baize.Tools` targets `net9.0` and `net10.0` because its schema
generation uses the `System.Text.Json.Schema` APIs introduced in .NET 9.

## Install

```xml
<PackageReference Include="Penghou.Baize" Version="0.1.0" />
<!-- plus the client package for your provider(s) -->
<PackageReference Include="Penghou.Baize.OpenAi" Version="0.1.0" />
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

## Routing

`Penghou.Baize.Router` resolves a model name (or a `ModelStrategy`, with
fallback chains) to a concrete client from the `LlmRouting` configuration
section. Every model declares one or more endpoints; each endpoint pairs an
API style with its provider-specific settings, so one logical model can be
reached through several wire protocols:

```json
{
  "LlmRouting": {
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
            "ApiStyle": "OpenAi",
            "ProviderModel": "deepseek-chat",
            "BaseUrl": "https://api.deepseek.com/v1",
            "ApiKeyEnvVar": "DEEPSEEK_API_KEY",
            "Dialect": "DeepSeek"
          },
          {
            "ApiStyle": "Claude",
            "ProviderModel": "claude-sonnet-4-5",
            "ApiKeyEnvVar": "ANTHROPIC_API_KEY"
          }
        ]
      },
      {
        "Name": "qwen",
        "Endpoints": [
          {
            "ApiStyle": "Ollama",
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

Every model needs a unique `Name` and at least one endpoint. `ApiStyle`
selects the wire protocol (`OpenAi`, `Claude`, `Ollama`, or `Gemini`);
`BaseUrl` and `ApiKeyEnvVar` override the provider defaults. OpenAI-compatible
endpoints can declare a `Dialect` (`Standard` or `DeepSeek`) that controls
whether the explicit `thinking` toggle is sent; it defaults to `Standard` and
is never inferred from the model name.

The router resolves each endpoint's capabilities in three layers, from the
most conservative to the most specific:

1. **API-style defaults** — only what the wire protocol guarantees. The
   OpenAI-compatible defaults claim tool definitions and streaming tool-call
   arguments but *not* parallel tool calls, native structured output, or
   extended thinking, because a generic "OpenAI-compatible" server does not
   guarantee `response_format` or reasoning effort. Claude and Gemini claim
   their documented native features. Ollama claims nothing beyond plain text
   streaming, because tool and JSON support depend on the local model, not the
   protocol.
2. **A named profile** (optional) — declared in the `Profiles` section and
   referenced from an endpoint through `Profile`. Profiles opt specific models
   into capabilities the conservative style defaults do not claim, without
   duplicating them on every endpoint.
3. **Per-endpoint `Capabilities`** — override both the style defaults and any
   referenced profile; an omitted capability inherits from the profile or the
   style default.

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
endpoint wins) or by the `(name, ApiStyle)` pair:

```csharp
var services = new ServiceCollection();
services.AddHttpClient();
services.AddLlmRouting(configuration); // reads the "LlmRouting" section

await using var provider = services.BuildServiceProvider();
var lookup = provider.GetRequiredService<ILlmModelLookup>();

ILlmClient byName = lookup.GetClient("deepseek");
ILlmClient byNameAndStyle = lookup.GetClient("deepseek", ApiStyle.Claude);

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
stable `EndpointId`, the logical model name, and the API style - so applications
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

## License

MIT
