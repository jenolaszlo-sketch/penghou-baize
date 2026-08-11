# Getting started

Baize separates the provider-neutral request model, wire providers, and routing. Install `Penghou.Baize.Router` plus only the provider packages your application uses.

## Fluent configuration

```csharp
services.AddHttpClient();
services.AddOllamaLlmProvider();
services.AddLlmRouting(routes => routes
    .AddModel("local-coder", model => model
        .AddEndpoint("Ollama", endpoint => endpoint
            .WithId("local-qwen")
            .UseProviderModel("qwen2.5-coder:7b")
            .UseBaseUrl("http://localhost:11434")))
    .AddStrategy(ModelStrategy.Auto, "local-coder")
    .AddNamedRoute("coding", "local-coder")
    .ValidateEndpointsOnStart());
```

`coding` is an application-defined route name, not a media type or a Baize-reserved category. Name routes after workload policy (`low-cost`, `private`, `coding`) and call them explicitly with `CompleteRouteAsync` or `StreamRouteAsync`.

See the compiling [quick-start sample](../samples/Penghou.Baize.QuickStart).

## Configuration files

JSON configuration remains useful when routes must reload at runtime:

```csharp
services.AddHttpClient();
services.AddOllamaLlmProvider();
services.AddLlmRouting(configuration);
services.AddLlmEndpointValidationOnStart();
```

Both forms build and validate the same `LlmRoutingOptions` graph. Fluent configuration is static; configuration-file registration supports atomic reloads and keeps the last valid snapshot when a reload is invalid.

## Explain before calling

```csharp
var explanation = await router.ExplainRouteAsync("coding", request);

foreach (var candidate in explanation.Candidates)
    Console.WriteLine($"{candidate.Endpoint.EndpointId}: {candidate.RejectionReason ?? $"rank {candidate.Rank}"}");
```

Explanation performs capability filtering and ranking without sending the request. It includes safe routing-memory statistics but no prompts, responses, or credentials.
