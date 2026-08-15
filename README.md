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

Baize is deliberately a client and routing layer, not an agent framework or
workflow engine. Generated-media and real-time APIs have different lifecycle
requirements and are not forced into the chat response model. See
[scope and boundaries](docs/scope-and-boundaries.md) for the current limits and
planned client surfaces.

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
| `Penghou.Baize.Diagnostics` | Opt-in bounded HTTP request/response capture for troubleshooting |

The core, provider clients, router, batch coordinator, Extensions.AI adapter,
and repair tools support .NET 8. Provider-neutral tools additionally target
.NET 9 and .NET 10 so applications can stay on their host framework without
giving up schema-aware recovery.

On .NET 8, `Penghou.Baize.Tools` uses a reflection-based JSON Schema exporter
covering ordinary objects, collections, dictionaries, required members, and
descriptions. Applications targeting .NET 9 or later automatically use the
richer built-in `System.Text.Json.Schema` exporter, which provides better
support for advanced serialization and schema scenarios. No configuration or
code change is required when upgrading the application's target framework.

## Install

```xml
<PackageReference Include="Penghou.Baize" Version="0.2.0" />
<!-- plus the client package for your provider(s) -->
<PackageReference Include="Penghou.Baize.OpenAi" Version="0.2.0" />
```

## Documentation

- [What Baize is—and is not](docs/scope-and-boundaries.md)
- [Getting started and fluent routing](docs/getting-started.md)
- [Validation and troubleshooting](docs/validation-and-troubleshooting.md)
- [Coverage policy and package baselines](docs/coverage.md)
- [Live provider compatibility matrix](docs/live-provider-compatibility.md)
- [Live provider verification log](docs/live-provider-verification-log.md)
- Provider guides: [DeepSeek](docs/providers/deepseek.md) and
  [Gemini](docs/providers/gemini.md)
- [Generation client roadmap](docs/roadmap-generation-client.md)
- [Create an LLM provider package](docs/extensibility/custom-llm-provider.md)
- [Create a custom route provider](docs/extensibility/custom-route-provider.md)
- [Runnable quick-start sample](samples/Penghou.Baize.QuickStart)

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

When incremental events are not needed, Core can collect any direct client
without a Router dependency:

```csharp
var response = await client.CompleteAsync(request);
Console.WriteLine(response.Content);
```

`CompleteAsync` uses `ILlmCompletionClient` when a provider or custom gateway
offers a native non-streaming path; otherwise it drains `StreamAsync` through
the same canonical collector used by the router. Passing an `onDelta` callback
always retains the streaming path.

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
using Penghou.Baize.Tools;
using Penghou.Baize.Tools.Schema;
using System.Text.Json.Serialization;

public sealed class GetWeatherArguments
{
    [JsonPropertyName("city")]
    [SchemaDescription("The city whose current weather should be returned")]
    public required string City { get; init; }
}

var promptBuilder = new LlmPromptBuilder
{
    Messages = messages,
    Tools =
    [
        LlmToolFactory.Create<GetWeatherArguments>(
            "get_weather",
            "Returns the weather for a city")
    ]
};

var response = await router.CompleteStreamingAsync(
    ModelStrategy.ToolCall,
    promptBuilder);

foreach (var call in response.ToolCalls)
    Console.WriteLine($"{call.Name}: {call.ArgumentsJson}");
```

`LlmToolFactory.Create<TArguments>` generates and caches a
provider-compatible JSON Schema for the argument type. On .NET 9 and later it
uses `System.Text.Json.Schema`; the .NET 8 fallback supports ordinary objects,
collections, dictionaries, required members, `JsonPropertyName`, and
`SchemaDescription`. Generate the string directly when it needs further
inspection or composition:

```csharp
var schemaJson = JsonSchemaGenerator
    .GenerateSchemaJson<GetWeatherArguments>();
var tool = new LlmTool("get_weather", "Returns the weather", schemaJson);
```

The generic type represents tool arguments, not the tool's return value.
Keep the explicit `LlmTool` constructor for externally supplied or manually
composed schemas.

Routing strategies are selection hints, not request shapes. Applications that
already have a canonical request can route it directly without rebuilding it
or pretending it belongs to a particular strategy:

```csharp
var request = new LlmRequest(messages, tools: tools);
var response = await router.CompleteStreamingAsync("deepseek", request);
```

Tools and structured output may be requested together, but only endpoints that
explicitly advertise `ToolsWithStructuredOutput` are eligible. Supporting
tool calls and structured output separately does not imply that their provider
API supports the combination.

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

For router-created clients, repair can instead be enabled as an opt-in
decorator:

```csharp
services.AddLlmRouting(configuration);
services.AddLlmStructuredOutputRepair();
```

Direct clients can opt in without the router:

```csharp
var repairedClient = client.WithStructuredOutputRepair(repairer);
var response = await repairedClient.CompleteAsync(schemaRequest);
```

Schema-constrained responses are buffered until their complete JSON document
can be validated and repaired. The resulting stream and collected
`LlmResponse` carry `ContentWasRepaired`, `ContentRepairAttempts`, and detailed
diagnostics. Ordinary chat, schema-less JSON, and tool-only requests keep their
normal streaming behavior. Provider clients remain strict when this decorator
is not registered.

## Native batch inference

`Penghou.Baize.Batch` groups requests by configured endpoint and exposes the
provider's native asynchronous batch client without introducing an
orchestration-runtime dependency. OpenAI, Anthropic, and Gemini adapters support
native submission, polling, result retrieval, and cancellation according to
their advertised `BatchCapabilities`.

Register routing first, then batch planning:

```csharp
services.AddLlmRouting(configuration);
services.AddBaizeBatch(
    new BatchPlannerOptions
    {
        MaxItemsPerGroup = 1_000
    },
    new BatchCoordinatorOptions
    {
        MaxConcurrentSubmissions = 4
    });

var batches = provider.GetRequiredService<IBaizeBatchCoordinator>();
var handle = await batches.SubmitAsync(new BaizeBatchSubmission(
[
    new BaizeBatchRequest(
        "request-1",
        new LlmRequest([new LlmMessage("user", "Summarize this")]),
        Model: "gpt-batch")
]));
var status = await batches.WaitForCompletionAsync(
    handle,
    new BatchWaitOptions
    {
        PollInterval = TimeSpan.FromSeconds(10),
        MaxPollInterval = TimeSpan.FromMinutes(1),
        BackoffFactor = 1.5,
        JitterRatio = 0.1,
        MaxTransientFailures = 3,
        Timeout = TimeSpan.FromHours(24),
        Progress = new Progress<BatchPollingUpdate>(update =>
            Console.WriteLine($"Poll {update.PollNumber}: " +
                $"{update.Status?.State} {update.Error}"))
    });
var results = await batches.GetResultsAsync(handle);
```

Request IDs must be unique. Model names are preserved verbatim, including
colons; select a provider explicitly with `BaizeBatchRequest.CreateForProvider`
or the record's separate `Provider` property. Provider handles are validated so
they cannot accidentally be used with another provider adapter. Baize does not
currently provide durable workflow orchestration; applications should persist
the returned `BaizeBatchHandle` if polling must survive a process restart, then
pass that deserialized handle back to `WaitForCompletionAsync`,
`GetResultsAsync`, or `CancelAsync`. Status and result calls for physical parts
run concurrently. Polling honors provider retry guidance, applies bounded
backoff and jitter, and retries only transient availability/rate-limit errors.

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
        "NativeStructuredOutput": true,
        "ToolsWithStructuredOutput": false
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
            "ApiKeySecretName": "DEEPSEEK_API_KEY",
            "Dialect": "DeepSeek"
          },
          {
            "Provider": "Claude",
            "ProviderModel": "claude-sonnet-4-5",
            "ApiKeySecretName": "ANTHROPIC_API_KEY"
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
    },
    "NamedRoutes": {
      "low-cost": [ "qwen", "deepseek" ],
      "reasoning": [ "deepseek" ]
    }
  }
}
```

Named routes are application-defined fallback chains and are deliberately
distinct from model names:

```csharp
var response = await router.CompleteRouteAsync(
    "low-cost",
    request);
```

Use `StreamAsync("qwen", request)` to target a model registration and
`StreamRouteAsync("low-cost", request)` to target a named chain. Routes select
endpoints but do not alter the canonical request shape.

Request-level application context can be passed to custom route providers
without coupling Baize to ASP.NET, `AsyncLocal`, or another host:

```csharp
var request = new LlmRequest(
    messages,
    metadata: new Dictionary<string, object?>
    {
        ["acme.tenant-id"] = tenantId,
        ["acme.residency"] = "eu",
        ["acme.low-cost"] = true
    });
```

The metadata map is copied when the request is created and is available as
`LlmRoutingContext.Request.Metadata`. It is application context—not provider
request data—and Baize clients never serialize it onto provider APIs. Do not
store secrets in metadata; reusable libraries should namespace their keys.

Every model needs a unique `Name` and at least one endpoint. `Provider` is a
case-insensitive adapter key; built-in keys are `OpenAi`, `Claude`, `Ollama`,
and `Gemini`, while packages can define their own. The older `ApiStyle`
property remains accepted for built-in providers. The `BaseUrl` and
`ApiKeySecretName` properties override provider defaults. Provider-specific settings can be
placed in an endpoint's `Settings` object. For compatibility, OpenAI's
top-level `Dialect` (`Standard` or `DeepSeek`) and Claude's `ThinkingStyle`
are also forwarded as provider settings.

### Secret providers

`ApiKeySecretName` is a lookup key, not necessarily an environment-variable
name and never the secret value itself. Baize passes it to `ISecretProvider`.
The default `EnvironmentSecretProvider` reads a process environment variable
with that name, which makes the configuration above work without additional
registration.

Applications can resolve the same logical names from another source by
implementing `ISecretProvider`. For example, this implementation reads a
dedicated configuration section that could itself be populated by .NET user
secrets, Azure Key Vault configuration, or another configuration provider:

```csharp
public sealed class ConfigurationSecretProvider(IConfiguration configuration)
    : ISecretProvider
{
    public Task<string?> GetSecretAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(configuration[$"LlmSecrets:{name}"]);
    }
}

services.AddSingleton<ISecretProvider, ConfigurationSecretProvider>();
services.AddLlmRouting(configuration);
```

The same options can be bound from the default `Baize:Diagnostics` section:

```csharp
services.AddBaizeHttpDiagnostics(configuration);
```

```json
{
  "Baize": {
    "Diagnostics": {
      "Enabled": false,
      "DirectoryPath": "logs/baize/http",
      "MaxBodyBytes": 524288,
      "MaxRetainedSessions": 100
    }
  }
}
```

Register the custom provider before `AddLlmRouting`; Baize uses
`TryAddSingleton` so it will not replace an application-provided
implementation. A remote secret provider should return `null` when a name is
unknown and honor the supplied cancellation token. Baize fails endpoint
construction with a message naming the unresolved secret, without logging its
value.

Credential and provider construction are deferred so configuration reloads do
not block DI threads. Applications that prefer startup validation can warm all
endpoints without sending an inference request:

```csharp
var router = provider.GetRequiredService<ILlmRouter>();
var validation = await router.ValidateEndpointsAsync();

foreach (var endpoint in validation.Endpoints.Where(result => !result.Succeeded))
    Console.Error.WriteLine($"{endpoint.EndpointId}: {endpoint.Error}");
```

This resolves secrets and constructs chat and advertised native-batch clients.
It does not transmit prompts or test model inference.

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

The shortest explicit provider registration looks like this:

```csharp
public sealed class AcmeProvider : ILlmClientProvider
{
    public LlmProviderKey Key => new("Acme");
    public string DefaultBaseUrl => "https://llm.acme.test/v1";
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new();
    public ILlmClient CreateClient(LlmClientProviderContext context) =>
        new AcmeClient(context);
}

services.AddSingleton<ILlmClientProvider, AcmeProvider>();
services.AddLlmRouting(configuration);
```

Provider packages should expose a small `AddAcmeLlmProvider` extension and
claim conservative capabilities. See the full [provider creation
guide](docs/extensibility/custom-llm-provider.md) for discovery, trimming,
validation, streaming, and error-handling guidance.

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
compatible endpoints are tried before a transiently failed route is retried.
By default the router makes at most two passes, with bounded exponential
backoff and provider `Retry-After` hints when available. Deterministic invalid
request, authentication, and content failures are not retried. All attempts share a
single per-request `RequestTimeout` deadline, so retries cannot multiply the
overall budget. Every stream ends with a
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

Retry behavior can be configured fluently:

```csharp
services.AddLlmRouting(routes => routes
    .WithRequestTimeout(TimeSpan.FromMinutes(2))
    .WithTransientRetries(
        maximumAttempts: 3,
        initialDelay: TimeSpan.FromSeconds(1),
        backoffFactor: 2,
        maximumDelay: TimeSpan.FromSeconds(30)));
```

Or through configuration:

```json
{
  "LlmRouting": {
    "Retry": {
      "MaximumAttempts": 3,
      "InitialDelay": "00:00:01",
      "BackoffFactor": 2,
      "MaximumDelay": "00:00:30"
    }
  }
}
```

Set `MaximumAttempts` to `1` to disable retry passes while retaining fallback
between distinct endpoints.

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
var endpoint = await router.ResolveAsync(ModelStrategy.StructuredOutput);

await memory.RecordCallAsync(endpoint.EndpointId);
await memory.RecordFailureAsync(
    endpoint.EndpointId,
    LlmFailureCategory.StructuredOutputMismatch);
```

`ResolveAsync` returns the `ResolvedEndpoint` the router would currently pick - its
stable `EndpointId`, the logical model name, and provider key - so applications
know where a stream is going and can attribute quality events correctly.
The synchronous `Resolve` overloads remain for source compatibility but are
obsolete because custom router-memory implementations may perform asynchronous
I/O.
Routing memory and cooldowns are keyed by endpoint id, so two endpoints of the
same logical model keep separate stats (give them distinct `Id` values in
configuration; an explicit id also keeps stats stable across renames or
reordering).

Availability failures are counted within a sliding window; call counts and
feature-reliability failures are cumulative. The package ships an in-memory
implementation (`InMemoryLlmRouterMemory`). To use durable or shared storage
(Redis, a database, ...), implement `ILlmRouterMemory` over an application
store. This example keeps the storage contract explicit so the backing system
can update counters and cooldowns atomically:

```csharp
public interface IRouterStatsStore
{
    Task IncrementCallsAsync(string endpointId, CancellationToken cancellationToken);

    Task RecordFailureAsync(
        string endpointId,
        LlmFailureCategory category,
        DateTimeOffset? unavailableUntil,
        CancellationToken cancellationToken);

    Task<LlmEndpointStats?> GetStatsAsync(
        string endpointId,
        CancellationToken cancellationToken);
}

public sealed class DurableRouterMemory(IRouterStatsStore store)
    : ILlmRouterMemory
{
    public Task RecordCallAsync(
        string endpointId,
        CancellationToken cancellationToken = default) =>
        store.IncrementCallsAsync(endpointId, cancellationToken);

    public Task RecordFailureAsync(
        string endpointId,
        LlmFailureCategory category,
        DateTimeOffset? unavailableUntil = null,
        CancellationToken cancellationToken = default) =>
        store.RecordFailureAsync(
            endpointId,
            category,
            unavailableUntil,
            cancellationToken);

    public async Task<LlmEndpointStats> GetStatsAsync(
        string endpointId,
        CancellationToken cancellationToken = default) =>
        await store.GetStatsAsync(endpointId, cancellationToken)
            ?? new LlmEndpointStats(endpointId, 0, 0, 0, 0);
}

services.AddSingleton<ILlmRouterMemory, DurableRouterMemory>();
services.AddLlmRouting(configuration);
```

Register custom memory before `AddLlmRouting` for the same `TryAddSingleton`
behavior. Implementations should make increments atomic, preserve the
`UnavailableUntil` cooldown, and apply the desired availability-failure
window. Router memory stores endpoint reliability statistics only; it is not a
prompt or response cache.

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

For policy that replaces route resolution itself, implement
`ILlmRouteProvider`. Derive from `LlmRouteProviderBase` when the policy needs
the replaceable router memory, then register it through DI:

```csharp
public sealed class TenantRouteProvider(ILlmRouterMemory memory)
    : LlmRouteProviderBase(memory)
{
    public override ValueTask<LlmRouteResolution> ResolveAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken = default) =>
        ResolveForTenantAsync(context, cancellationToken);
}

services.AddSingleton<ILlmRouteProvider, TenantRouteProvider>();
services.AddLlmRouting(configuration);
```

The router continues to own execution, fallback safety, diagnostics, and
memory updates. See the [custom route provider
guide](docs/extensibility/custom-route-provider.md) for the complete contract.

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
Endpoint validation, deterministic JSON repair, batch submission/waiting,
adaptive polling, and transient batch failures use the same source. Tags are
limited to operation, provider, model, endpoint, outcome, and error type;
Baize does not attach prompts, responses, schemas, or credentials.
Register that source with OpenTelemetry in the host application:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(BaizeTelemetry.InstrumentationName))
    .WithMetrics(metrics => metrics.AddMeter(BaizeTelemetry.InstrumentationName));
```

Configuration reloads build a complete immutable routing runtime before one
atomic swap. The router, model lookup, endpoint validator, strategy chains, and
request limits therefore move to the same configuration version together;
in-flight requests continue on the snapshot with which they started.

### Troubleshooting captures

`Penghou.Baize.Diagnostics` provides the raw transport evidence needed when a
provider changes its streaming format, returns malformed JSON, or produces an
unexpected tool call. Installing the package does not enable capture. Register
it explicitly and set `Enabled`:

```csharp
using Penghou.Baize.Diagnostics;

services.AddLogging();
services.AddBaizeHttpDiagnostics(options =>
{
    options.Enabled = true;
    options.DirectoryPath = "logs/baize/http";
    options.MaxBodyBytes = 512 * 1024;
    options.MaxRetainedSessions = 100;
    options.CaptureRequestBody = true;
    options.CaptureResponseBody = true;
});
services.AddLlmRouting(configuration);
```

Each call creates correlated `.request.log`, `.response.log`, and bounded
`.response.raw` files. Responses are copied incrementally while the provider
client consumes the stream, so diagnostics do not require buffering the model
response. Authorization, cookie, API-key headers, and common credential query
parameters are always redacted. Request and response bodies can nevertheless
contain prompts, generated content, personal data, tool arguments, or inline
media. Keep the directory private and enable capture only while troubleshooting.

Capture failures are warning logs and do not break inference by default. Set
`ContinueOnCaptureError` to `false` when a diagnostic artifact is mandatory.
`FlushEachResponseChunk` improves crash investigations at a throughput cost.
Relative directories are resolved from `AppContext.BaseDirectory`.

The package emits structured debug/warning logs and the following instruments
through `BaizeTelemetry.InstrumentationName` without putting body content in
logs, spans, or metric tags:

- `llm.http.capture` activities;
- `baize.diagnostics.sessions` and `baize.diagnostics.failures` counters;
- captured-byte and truncated-body counters;
- capture-duration histograms.

The router also logs deferred provider construction, endpoint validation, and
configuration reload outcomes. Invalid reloads retain the last good atomic
routing snapshot and increment `baize.router.configuration.reload_failures`.
Configured provider-module discovery emits `llm.provider.module.load` spans
and load/failure/duration metrics, which makes missing or incompatible plugin
assemblies visible during integration-test startup.

### Live provider tests

`Penghou.Baize.IntegrationTests.slnx` is deliberately separate from the main
solution and CI workflow. It sends real requests and uses the same public
logging, telemetry, routing, secret-provider, and HTTP diagnostics setup that
applications use. Configure one provider/model explicitly:

```powershell
$env:BAIZE_RUN_LIVE_TESTS = "1"
$env:BAIZE_LIVE_PROVIDER = "Gemini"
$env:BAIZE_LIVE_MODEL = "your-gemini-model"
$env:GEMINI_API_KEY = "your-key"

dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release
```

Run only one paid capability while developing it:

```powershell
# Baseline streaming only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=Baseline"

# Native tools only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=Tools"

# Sequential multi-turn tool round trip
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=ComplexTools"

# Parallel tool calls in one model turn
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=ParallelTools"

# Structured output only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=StructuredOutput"

# Image input only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=ImageInput"

# Audio input only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=AudioInput"

# Video input only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=VideoInput"

# PDF/file input only
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=FileInput"

# Paid Gemini Interactions API image-generation probe
$env:BAIZE_LIVE_TEST_IMAGE_GENERATION = "1"
$env:BAIZE_LIVE_IMAGE_MODEL = "gemini-3.1-flash-lite-image"
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=ImageGeneration"

# Native batch only (when live batch coverage is enabled)
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter "Category=Live&Capability=Batch"
```

Omit `--filter` for the complete live suite. Filters affect test execution, so
unselected capabilities make no provider calls and consume no model tokens.

For local development, copy `.env.example` to the ignored `.env.local` file
and fill in the credential instead. The live-test harness finds that file from
the repository tree and never overwrites environment variables already
provided by the shell or CI.

Supported provider values are `OpenAi`, `Claude`, `Gemini`, and `Ollama`.
Use `BAIZE_LIVE_BASE_URL` for compatible gateways or local servers and
`BAIZE_LIVE_SECRET_NAME` when the credential has a different environment
variable name. Use `BAIZE_LIVE_DIALECT=DeepSeek` when exercising DeepSeek
through the OpenAI-compatible provider so the request and reasoning stream use
the correct dialect. Set `BAIZE_LIVE_TEST_TOOLS=1` to additionally run the native
tool-call contract test. Set `BAIZE_LIVE_TEST_THINKING=1` to opt into the
larger-budget thinking test; the baseline smoke test leaves provider thinking
at its default and reserves enough output budget for thinking-first models.
The live harness defaults its named HTTP client to a five-minute timeout;
override it with `BAIZE_LIVE_HTTP_TIMEOUT_SECONDS` when testing especially
slow or long-running models.
Set `BAIZE_LIVE_TEST_BATCH=1` to opt into native batch submission and polling,
which can run substantially longer than synchronous tests.
Set `BAIZE_LIVE_TEST_IMAGE_GENERATION=1` to opt into the paid Gemini provider
probe, with `BAIZE_LIVE_IMAGE_MODEL` selecting its image model. This probe
validates the provider contract without claiming that `ILlmClient` can return
binary artifacts; that portable surface remains planned for GenerationClient.
The tests print Baize activities and metrics and keep
the correlated raw transport artifacts under
`tests/Penghou.Baize.IntegrationTests/bin/.../artifacts/live-diagnostics` by
default. Without `BAIZE_RUN_LIVE_TESTS=1`, every live test is skipped.

## License

MIT
