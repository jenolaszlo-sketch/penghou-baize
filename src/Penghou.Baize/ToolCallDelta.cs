namespace Penghou.Baize;

/// <summary>A streaming fragment of a tool call being assembled by the model.</summary>
/// <param name="Index">The index of the tool call within the response.</param>
/// <param name="Id">The provider's identifier for the tool call, when available.</param>
/// <param name="Name">The name of the tool being called.</param>
/// <param name="ArgumentsJsonFragment">A fragment of the tool call arguments JSON.</param>
public sealed record ToolCallDelta(
    int Index,
    string? Id = null,
    string? Name = null,
    string? ArgumentsJsonFragment = null);
