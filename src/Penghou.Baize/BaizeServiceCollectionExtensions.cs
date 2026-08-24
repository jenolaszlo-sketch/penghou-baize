using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Penghou.Baize;

/// <summary>
/// Core dependency-injection setup for Penghou.Baize.
/// </summary>
public static class BaizeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared <c>llm</c> named <see cref="System.Net.Http.HttpClient"/>
    /// transport owned by the core package: every provider, generation client,
    /// and batch adapter consumes this named client. The registration applies a
    /// conservative default request timeout (100 seconds); the optional
    /// Diagnostics package layers traffic capture on top of it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the named-client builder.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBaizeTransport(
        this IServiceCollection services,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(BaizeTransportRegistrationMarker)))
        {
            services.AddSingleton<BaizeTransportRegistrationMarker>();
            var builder = services.AddHttpClient("llm")
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));
            builder.ConfigureHttpClient(client =>
                client.Timeout = TimeSpan.FromSeconds(100));
            configure?.Invoke(builder);
        }

        return services;
    }
}

internal sealed class BaizeTransportRegistrationMarker;
