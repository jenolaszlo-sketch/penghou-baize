# Create an LLM provider package

Implement `ILlmClientProvider` in a package that references `Penghou.Baize`. Choose a stable, unique provider key and claim only capabilities guaranteed by the wire adapter.

```csharp
public sealed class AcmeLlmProvider : ILlmClientProvider
{
    public LlmProviderKey Key => new("Acme");
    public string DefaultBaseUrl => "https://llm.acme.test/v1";
    public LlmEndpointCapabilities DefaultCapabilities { get; } = new();

    public ILlmClient CreateClient(LlmClientProviderContext context) =>
        new AcmeLlmClient(context);
}

public static class AcmeServiceCollectionExtensions
{
    public static IServiceCollection AddAcmeLlmProvider(this IServiceCollection services)
    {
        services.AddSingleton<ILlmClientProvider, AcmeLlmProvider>();
        return services;
    }
}
```

Consumers can explicitly register the package, which is the trimming- and AOT-friendly path:

```csharp
services.AddAcmeLlmProvider();
services.AddLlmRouting(configuration);
```

Alternatively, configuration can list the provider assembly under `ProviderModules`. The package must still be referenced so its DLL and dependencies are copied to output. Module discovery accepts assembly identities, never filesystem paths, and should be treated as a trusted-code boundary.

Provider clients should validate requests against their effective capabilities, stream canonical `LlmStreamEvent` values, preserve cancellation, populate usage/diagnostics when the API supplies them, and throw `LlmClientException` with safe provider failure details.
