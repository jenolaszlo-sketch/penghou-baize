namespace Penghou.Baize.Tools;

/// <summary>
/// Recovers "pseudo" tool calls that a model emitted as free-form text
/// instead of native tool calling (a common failure mode of smaller and
/// instruction-tuned models), repairing the embedded JSON along the way.
/// </summary>
public interface IContentToolCallExtractor
{
    /// <summary>
    /// Extracts tool calls from the given model content.
    /// </summary>
    /// <param name="content">The raw model output; may be null or empty.</param>
    /// <param name="tools">The declared tools the calls are matched against.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// The recovered tool calls with repaired JSON arguments, or an empty
    /// list when no calls are found.
    /// </returns>
    Task<IReadOnlyList<LlmToolCall>> ExtractAsync(
        string? content,
        IReadOnlyCollection<LlmTool> tools,
        CancellationToken cancellationToken = default);
}
