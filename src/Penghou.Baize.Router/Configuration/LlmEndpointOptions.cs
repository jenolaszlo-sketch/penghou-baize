using Penghou.Baize.Claude;
using Penghou.Baize.OpenAi;

namespace Penghou.Baize.Router.Configuration;

/// <summary>A single reachable endpoint: an API style plus provider settings.</summary>
public sealed class LlmEndpointOptions
{
    /// <summary>
    /// An explicit, stable identifier for the endpoint. When omitted, the
    /// router derives one from the model name, API style, and registration
    /// order. Give two endpoints of the same logical model distinct ids (for
    /// example "primary-gateway" and "backup-gateway") so routing memory and
    /// cooldowns are tracked separately.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>The wire protocol used to reach the provider.</summary>
    public ApiStyle ApiStyle { get; init; }

    /// <summary>
    /// The OpenAI-compatible wire dialect; only meaningful for
    /// <see cref="ApiStyle.OpenAi"/> endpoints, defaults to
    /// <see cref="OpenAiDialect.Standard"/>.
    /// </summary>
    public OpenAiDialect? Dialect { get; init; }

    /// <summary>
    /// The Claude extended-thinking contract; only meaningful for
    /// <see cref="ApiStyle.Claude"/> endpoints, defaults to
    /// <see cref="ClaudeThinkingStyle.Adaptive"/>.
    /// </summary>
    public ClaudeThinkingStyle? ThinkingStyle { get; init; }

    /// <summary>The provider-specific model identifier; defaults to the model name.</summary>
    public string? ProviderModel { get; init; }

    /// <summary>The provider base URL; defaults to the API style's default.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The environment variable holding the API key; when empty or unset, no
    /// key is sent (for example local Ollama).
    /// </summary>
    public string? ApiKeyEnvVar { get; init; }

    /// <summary>
    /// Per-endpoint capability overrides; each null member inherits the API
    /// style's default.
    /// </summary>
    public LlmEndpointCapabilitiesOptions? Capabilities { get; init; }

    /// <summary>
    /// The name of a capability profile declared in
    /// <see cref="LlmRoutingOptions.Profiles"/>. A referenced profile is
    /// overlaid on the API style's conservative defaults before
    /// <see cref="Capabilities"/> is applied. Null keeps the style defaults
    /// (and any <see cref="Capabilities"/> overrides).
    /// </summary>
    public string? Profile { get; init; }
}
