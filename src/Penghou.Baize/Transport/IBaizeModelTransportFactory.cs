using Penghou.Model.Abstractions;

namespace Penghou.Baize;

/// <summary>Creates the semantic model transports used by Baize clients.</summary>
/// <remarks>Factories may wrap the supplied trusted default transport to observe, replace, or deny calls.</remarks>
public interface IBaizeModelTransportFactory
{
    /// <summary>Wraps a typed one-shot model transport for a configured target.</summary>
    IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(
        ModelTarget target,
        IModelTransport<TRequest, TResponse> trustedDefault)
        where TRequest : notnull
        where TResponse : notnull;

    /// <summary>Wraps a typed streaming model transport for a configured target.</summary>
    IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(
        ModelTarget target,
        IStreamingModelTransport<TRequest, TEvent> trustedDefault)
        where TRequest : notnull
        where TEvent : notnull;
}

/// <summary>Default Baize factory that returns the trusted provider transports unchanged.</summary>
public sealed class PassThroughBaizeModelTransportFactory : IBaizeModelTransportFactory
{
    /// <summary>The shared pass-through instance.</summary>
    public static PassThroughBaizeModelTransportFactory Instance { get; } = new();

    /// <summary>Creates the pass-through factory.</summary>
    public PassThroughBaizeModelTransportFactory() { }

    /// <inheritdoc />
    public IModelTransport<TRequest, TResponse> Wrap<TRequest, TResponse>(
        ModelTarget target,
        IModelTransport<TRequest, TResponse> trustedDefault)
        where TRequest : notnull
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        return trustedDefault ?? throw new ArgumentNullException(nameof(trustedDefault));
    }

    /// <inheritdoc />
    public IStreamingModelTransport<TRequest, TEvent> WrapStreaming<TRequest, TEvent>(
        ModelTarget target,
        IStreamingModelTransport<TRequest, TEvent> trustedDefault)
        where TRequest : notnull
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        return trustedDefault ?? throw new ArgumentNullException(nameof(trustedDefault));
    }
}
