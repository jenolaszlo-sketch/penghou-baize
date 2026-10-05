using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Penghou.Http.Abstractions;

namespace Penghou.Baize;

/// <summary>
/// Core dependency-injection setup for Penghou.Baize.
/// </summary>
public static class BaizeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared <c>llm</c> named HTTP client and the replaceable default neutral HTTP transport.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the named-client builder.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBaizeTransport(
        this IServiceCollection services,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(BaizeTransportRegistrationMarker)))
        {
            services.AddSingleton<BaizeTransportRegistrationMarker>();
            var builder = services.AddHttpClient("llm")
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            builder.ConfigureHttpClient(client => client.Timeout = BaizeHttp.DefaultTimeout);
            configure?.Invoke(builder);
        }

        services.TryAddSingleton<IHttpTransport, BaizeHttpTransport>();
        return services;
    }
}

internal sealed class BaizeTransportRegistrationMarker;
