namespace Penghou.Baize;

/// <summary>Describes a tool a model may call.</summary>
/// <param name="Name">The tool's unique name.</param>
/// <param name="Description">A natural-language description of when to use the tool.</param>
/// <param name="InputSchemaJson">The JSON schema describing the tool's arguments.</param>
/// <param name="Strict">
/// Whether the provider must enforce the declared argument schema. Endpoints
/// must explicitly advertise strict-tool support before such a request can be
/// routed or sent.
/// </param>
public sealed record LlmTool(
    string Name,
    string Description,
    string InputSchemaJson,
    bool Strict = false);
