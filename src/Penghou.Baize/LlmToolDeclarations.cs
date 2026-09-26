namespace Penghou.Baize;

/// <summary>
/// Shared tool-declaration integrity for every Baize boundary that accepts a
/// tool set: requests, normalization, and extraction. Blank and duplicate
/// tool names are rejected before provider I/O or repair so declaration
/// order can never select which duplicate schema wins. Comparison is ordinal:
/// tool names are case-sensitive identifiers on every supported provider.
/// </summary>
public static class LlmToolDeclarations
{
    /// <summary>Validates a tool declaration set.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tools"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a declaration is null, has a blank name, or duplicates an
    /// earlier name. The message names the tool but never its arguments.
    /// </exception>
    public static void Validate(IReadOnlyCollection<LlmTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ArgumentException(
                    $"Tool declaration at index {index} is null.",
                    nameof(tools));
            }

            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                throw new ArgumentException(
                    $"Tool declaration at index {index} has a blank name.",
                    nameof(tools));
            }

            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException(
                    $"Duplicate tool declaration '{tool.Name}'.",
                    nameof(tools));
            }

            index++;
        }
    }
}
