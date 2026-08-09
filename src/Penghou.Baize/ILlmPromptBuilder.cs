namespace Penghou.Baize;

/// <summary>
/// Shapes an <see cref="LlmRequest"/> for a given <see cref="ModelStrategy"/>.
/// </summary>
public interface ILlmPromptBuilder
{
    /// <summary>
    /// Builds a request appropriate for the given <paramref name="strategy"/>.
    /// </summary>
    /// <param name="strategy">The capability the request is targeting.</param>
    /// <returns>The built request.</returns>
    LlmRequest Build(ModelStrategy strategy);
}
