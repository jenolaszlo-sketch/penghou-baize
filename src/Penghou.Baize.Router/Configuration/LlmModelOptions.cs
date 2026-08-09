namespace Penghou.Baize.Router.Configuration;

/// <summary>A logical model registration with a unique name and its endpoints.</summary>
public sealed class LlmModelOptions
{
    /// <summary>The model's unique registration name used in lookups and fallback chains.</summary>
    public string Name { get; init; } = default!;

    /// <summary>The endpoints the model can be reached through.</summary>
    public List<LlmEndpointOptions> Endpoints { get; init; } = [];
}
