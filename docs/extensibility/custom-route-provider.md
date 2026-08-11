# Create a route provider

Implement `ILlmRouteProvider` when the configured fallback chains are not enough—for example tenant-aware, residency-aware, budget-aware, or experiment routing. `LlmRouteProviderBase` provides access to the replaceable `ILlmRouterMemory` and helpers for endpoint statistics and cooldowns.

```csharp
public sealed class TenantRouteProvider(ILlmRouterMemory memory)
    : LlmRouteProviderBase(memory)
{
    public override async ValueTask<LlmRouteResolution> ResolveAsync(
        LlmRoutingContext context,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveTenantEndpoint(context); // your application policy
        var stats = await GetStatsAsync(endpoint, cancellationToken);
        var candidate = new LlmRouteCandidateExplanation(
            endpoint, true, null, 0, stats);
        return new LlmRouteResolution(
            [endpoint],
            new LlmRouteExplanation(
                context.Target,
                [endpoint.Model],
                [candidate],
                endpoint));
    }
}
```

Register it with DI before routing is first resolved:

```csharp
services.AddSingleton<ILlmRouterMemory, DurableRouterMemory>(); // optional
services.AddSingleton<ILlmRouteProvider, TenantRouteProvider>();
services.AddLlmRouting(configuration);
```

The provider returns an ordered list of configured endpoint IDs. `LlmRouter` still owns request execution, fallback safety, timeouts, diagnostics, and memory updates. Route providers should not transmit requests. Throw `LlmRoutingException` for actionable routing failures and preserve the target and considered candidates.
