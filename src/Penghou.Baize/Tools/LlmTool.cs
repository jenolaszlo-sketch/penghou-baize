namespace Penghou.Baize;

/// <summary>Describes a tool a model may call.</summary>
/// <param name="Name">The tool's unique name.</param>
/// <param name="Description">A natural-language description of when to use the tool.</param>
/// <param name="InputSchemaJson">The JSON schema describing the tool's arguments.</param>
public sealed record LlmTool(
    string Name,
    string Description,
    string InputSchemaJson);
