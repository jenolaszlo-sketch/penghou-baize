namespace Penghou.Baize;

/// <summary>Provider-neutral inputs required to construct an LLM client.</summary>
/// <param name="Model">The provider-specific model identifier.</param>
/// <param name="HttpClientFactory">The application HTTP client factory.</param>
/// <param name="ApiKey">The resolved API key, or an empty string when none is required.</param>
/// <param name="BaseUrl">The provider endpoint base URL.</param>
/// <param name="Capabilities">The effective endpoint capabilities.</param>
/// <param name="Settings">Provider-specific settings from trusted application configuration.</param>
public sealed record LlmClientProviderContext(
    string Model,
    IHttpClientFactory HttpClientFactory,
    string ApiKey,
    string BaseUrl,
    LlmEndpointCapabilities Capabilities,
    IReadOnlyDictionary<string, string> Settings);
