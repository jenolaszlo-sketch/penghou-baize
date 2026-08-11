namespace Penghou.Baize;

/// <summary>Provider endpoint information used while adapting a canonical schema.</summary>
/// <param name="Provider">The provider/API dialect receiving the schema.</param>
/// <param name="Model">The provider-specific model identifier.</param>
/// <param name="ApiVersion">The provider API version, when known.</param>
/// <param name="Purpose">How the schema is used on the provider API.</param>
public sealed record LlmSchemaAdaptationContext(
    LlmProviderKey Provider,
    string Model,
    string? ApiVersion,
    LlmSchemaPurpose Purpose);
