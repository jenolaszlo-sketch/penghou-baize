using System.Net.Http;
using Penghou.Http.Abstractions;

namespace Penghou.Baize;

/// <summary>HTTP transport helpers shared across providers.</summary>
public static class BaizeHttp
{
    /// <summary>
    /// The shared named HttpClient every Baize transport consumer obtains
    /// through <c>IHttpClientFactory.CreateClient</c>. Registered by core via
    /// <c>AddBaizeTransport</c>; the optional Diagnostics package layers
    /// traffic capture on top.
    /// </summary>
    public const string ClientName = "llm";

    /// <summary>The default request timeout applied by <c>AddBaizeTransport</c>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);
    /// <summary>
    /// Wraps an <see cref="IHttpClientFactory"/> so every client it creates
    /// carries the supplied per-request timeout. The global transport default
    /// (registered by <c>AddBaizeTransport</c>) stays in force until the
    /// wrapped factory is consulted; because <c>CreateClient</c> returns a
    /// fresh <see cref="HttpClient"/> over pooled handlers, overriding
    /// <see cref="HttpClient.Timeout"/> per model/endpoint never affects other
    /// consumers of the same named client.
    /// </summary>
    /// <param name="factory">The application HTTP client factory.</param>
    /// <param name="timeout">The request timeout; must be positive and within the neutral transport timeout profile.</param>
    /// <returns>A factory whose clients enforce <paramref name="timeout"/>.</returns>
    public static IHttpClientFactory WithRequestTimeout(
        this IHttpClientFactory factory,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        return factory is NeutralTransportClientFactory neutral
            ? new NeutralTransportClientFactory(
                neutral.Transport is BaizeHttpTransport direct
                    ? new BaizeHttpTransport(new TimeoutHttpClientFactory(direct.ClientFactory, timeout))
                    : neutral.Transport, timeout)
            : new TimeoutHttpClientFactory(factory, timeout);
    }

    /// <summary>Creates an HTTP client factory whose sends cross the supplied neutral transport.</summary>
    /// <remarks>The factory carries no ambient cookies or credentials.</remarks>
    public static IHttpClientFactory CreateClientFactory(IHttpTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        // The default backend enforces the configured named-client timeout through body reads.
        // Avoid imposing a second 100-second ceiling on a host-configured longer timeout.
        return new NeutralTransportClientFactory(transport,
            transport is BaizeHttpTransport ? TimeSpan.FromDays(1) : DefaultTimeout);
    }

    internal static IHttpClientFactory EnsureTransportFactory(IHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory is NeutralTransportClientFactory ? factory : CreateClientFactory(new BaizeHttpTransport(factory));
    }

    private sealed class NeutralTransportClientFactory(IHttpTransport transport, TimeSpan timeout) : IHttpClientFactory
    {
        public IHttpTransport Transport { get; } = transport;
        public HttpClient CreateClient(string name) => new(new NeutralTransportHandler(Transport, timeout), disposeHandler: true)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class NeutralTransportHandler(IHttpTransport transport, TimeSpan timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            BaizeHttpTransportAdapter.SendAsync(transport, request, new HttpTransportLimits(timeout: timeout), null, cancellationToken);
    }

    private sealed class TimeoutHttpClientFactory(
        IHttpClientFactory inner,
        TimeSpan timeout)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = inner.CreateClient(name);
            client.Timeout = timeout;
            return client;
        }
    }
}
