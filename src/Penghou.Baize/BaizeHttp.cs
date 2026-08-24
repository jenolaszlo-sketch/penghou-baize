using System.Net.Http;

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
    /// <param name="timeout">The request timeout; must be positive or infinite.</param>
    /// <returns>A factory whose clients enforce <paramref name="timeout"/>.</returns>
    public static IHttpClientFactory WithRequestTimeout(
        this IHttpClientFactory factory,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        return new TimeoutHttpClientFactory(factory, timeout);
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
