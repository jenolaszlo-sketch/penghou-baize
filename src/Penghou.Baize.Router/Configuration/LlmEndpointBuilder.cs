namespace Penghou.Baize.Router.Configuration;

/// <summary>Builds one provider endpoint.</summary>
public sealed class LlmEndpointBuilder(string provider)
{
    private string? _id;
    private string? _providerModel;
    private string? _baseUrl;
    private string? _secretName;
    private string? _profile;
    private readonly Dictionary<string, string> _settings =
        new(StringComparer.OrdinalIgnoreCase);
    private LlmEndpointCapabilitiesOptions? _capabilities;

    /// <summary>Sets the stable endpoint identifier.</summary>
    public LlmEndpointBuilder WithId(string id) { _id = id; return this; }
    /// <summary>Sets the provider-specific model identifier.</summary>
    public LlmEndpointBuilder UseProviderModel(string model) { _providerModel = model; return this; }
    /// <summary>Overrides the provider's default base URL.</summary>
    public LlmEndpointBuilder UseBaseUrl(string baseUrl) { _baseUrl = baseUrl; return this; }
    /// <summary>Sets the name resolved through <see cref="ISecretProvider"/>.</summary>
    public LlmEndpointBuilder UseSecret(string secretName) { _secretName = secretName; return this; }
    /// <summary>Applies a named capability profile.</summary>
    public LlmEndpointBuilder UseProfile(string profile) { _profile = profile; return this; }

    /// <summary>Adds a provider-specific setting.</summary>
    public LlmEndpointBuilder WithSetting(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _settings.Add(key, value);
        return this;
    }

    /// <summary>Overrides conservative provider capabilities for this endpoint.</summary>
    public LlmEndpointBuilder ConfigureCapabilities(
        Action<LlmEndpointCapabilitiesBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LlmEndpointCapabilitiesBuilder();
        configure(builder);
        _capabilities = builder.Build();
        return this;
    }

    internal LlmEndpointOptions Build() => new()
    {
        Provider = provider,
        Id = _id,
        ProviderModel = _providerModel,
        BaseUrl = _baseUrl,
        ApiKeySecretName = _secretName,
        Profile = _profile,
        Settings = _settings,
        Capabilities = _capabilities
    };
}
