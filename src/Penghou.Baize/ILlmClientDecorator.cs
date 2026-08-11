namespace Penghou.Baize;

/// <summary>
/// Decorates configured endpoint clients as they are added to a router
/// snapshot. Decorators are applied in dependency-injection registration
/// order and should preserve endpoint capabilities and metadata.
/// </summary>
public interface ILlmClientDecorator
{
    /// <summary>Wraps a configured endpoint client.</summary>
    ILlmClient Decorate(ILlmClient client);
}
