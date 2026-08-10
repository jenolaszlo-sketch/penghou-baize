namespace Penghou.Baize.Router.Configuration;

/// <summary>
/// A trusted provider assembly to load into the default application load
/// context. Filesystem paths are deliberately not supported.
/// </summary>
public sealed class LlmProviderModuleOptions
{
    /// <summary>The assembly's simple or display name.</summary>
    public string Assembly { get; init; } = default!;

    /// <summary>
    /// Optional full type name implementing
    /// <see cref="ILlmClientProvider"/>. When omitted, every public concrete
    /// provider implementation in the assembly is registered.
    /// </summary>
    public string? Type { get; init; }
}
