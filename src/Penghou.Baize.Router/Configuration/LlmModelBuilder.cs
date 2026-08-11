namespace Penghou.Baize.Router.Configuration;

/// <summary>Builds the endpoints for one logical model.</summary>
public sealed class LlmModelBuilder(string name)
{
    private readonly List<LlmEndpointOptions> _endpoints = [];

    /// <summary>Adds an endpoint implemented by a registered provider.</summary>
    public LlmModelBuilder AddEndpoint(
        string provider,
        Action<LlmEndpointBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var builder = new LlmEndpointBuilder(provider);
        configure?.Invoke(builder);
        _endpoints.Add(builder.Build());
        return this;
    }

    internal LlmModelOptions Build() => new() { Name = name, Endpoints = _endpoints };
}
