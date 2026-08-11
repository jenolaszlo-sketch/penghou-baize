namespace Penghou.Baize;

/// <summary>Stable identity information exposed by a configured LLM client.</summary>
public sealed record LlmClientMetadata(
    string Provider,
    string Model,
    Uri? Endpoint = null,
    string? EndpointId = null);

/// <summary>Implemented by clients that can describe their configured endpoint.</summary>
public interface ILlmClientMetadataProvider
{
    /// <summary>The configured provider, model, and endpoint identity.</summary>
    LlmClientMetadata Metadata { get; }
}
