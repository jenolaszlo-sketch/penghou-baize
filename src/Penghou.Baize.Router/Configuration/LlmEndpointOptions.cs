namespace Penghou.Baize.Router.Configuration;

/// <summary>A single reachable endpoint and its provider settings.</summary>
public sealed class LlmEndpointOptions
{
    /// <summary>
    /// An explicit, stable identifier for the endpoint. When omitted, the
    /// router derives one from the model name and provider key. Give two
    /// endpoints of the same logical model and provider distinct ids (for
    /// example "primary-gateway" and "backup-gateway") so routing memory and
    /// cooldowns are tracked separately.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Extensible provider key. When omitted, the legacy <see cref="ApiStyle"/>
    /// value is used.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Legacy built-in provider selector. New integrations should use
    /// <see cref="Provider"/> so third-party adapters do not require enum changes.
    /// </summary>
    public ApiStyle ApiStyle { get; init; }

    /// <summary>
    /// Legacy convenience setting for OpenAI-compatible providers. Prefer
    /// <c>Settings:Dialect</c> for new configuration.
    /// </summary>
    public string? Dialect { get; init; }

    /// <summary>
    /// Legacy convenience setting for Claude providers. Prefer
    /// <c>Settings:ThinkingStyle</c> for new configuration.
    /// </summary>
    public string? ThinkingStyle { get; init; }

    /// <summary>
    /// Provider-specific settings. Keys are interpreted by the selected
    /// provider adapter and compared case-insensitively.
    /// </summary>
    public Dictionary<string, string> Settings { get; init; } = [];

    /// <summary>The provider-specific model identifier; defaults to the model name.</summary>
    public string? ProviderModel { get; init; }

    /// <summary>The provider base URL; defaults to the provider adapter's URL.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The name passed to <see cref="ISecretProvider"/> to resolve the API key;
    /// when empty or unset, no key is sent (for example local Ollama). The
    /// default secret provider treats this as an environment-variable name.
    /// </summary>
    public string? ApiKeySecretName { get; init; }

    /// <summary>
    /// Per-endpoint capability overrides; each null member inherits the API
    /// style's default.
    /// </summary>
    public LlmEndpointCapabilitiesOptions? Capabilities { get; init; }

    /// <summary>
    /// The per-model HTTP request timeout applied to every call made through
    /// this endpoint (for example <c>"00:02:00"</c> in configuration). When
    /// null the shared transport default applies. Long-generation models such
    /// as reasoning or large-context models can raise this without slowing
    /// every other endpoint down.
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; }

    /// <summary>
    /// The name of a capability profile declared in
    /// <see cref="LlmRoutingOptions.Profiles"/>. A referenced profile is
    /// overlaid on the provider's conservative defaults before
    /// <see cref="Capabilities"/> is applied. Null keeps the provider defaults
    /// (and any <see cref="Capabilities"/> overrides).
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>The effective extensible provider key for this endpoint.</summary>
    public LlmProviderKey ProviderKey =>
        new(Provider ?? ApiStyle.ToString());
}
