using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Penghou.Baize.Diagnostics;

/// <summary>Dependency-injection helpers for opt-in Baize diagnostics.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers bounded HTTP traffic capture for the <c>llm</c> named client.
    /// Capture remains disabled unless <see cref="HttpTrafficCaptureOptions.Enabled"/>
    /// is explicitly set.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional diagnostics configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddBaizeHttpDiagnostics(
        this IServiceCollection services,
        Action<HttpTrafficCaptureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<HttpTrafficCaptureOptions>();
        if (configure is not null)
            options.Configure(configure);
        return CompleteRegistration(services, options);
    }

    /// <summary>
    /// Registers HTTP traffic capture bound to a configuration section.
    /// Capture remains disabled when the section omits <c>Enabled</c>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration containing diagnostics settings.</param>
    /// <param name="sectionName">The section name; defaults to <c>Baize:Diagnostics</c>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddBaizeHttpDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Baize:Diagnostics")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var options = services.AddOptions<HttpTrafficCaptureOptions>()
            .Bind(configuration.GetSection(sectionName));
        return CompleteRegistration(services, options);
    }

    private static IServiceCollection CompleteRegistration(
        IServiceCollection services,
        OptionsBuilder<HttpTrafficCaptureOptions> options)
    {
        options.Validate(
                value => !value.Enabled ||
                         (!string.IsNullOrWhiteSpace(value.DirectoryPath) &&
                          value.MaxBodyBytes > 0 &&
                          value.MaxRetainedSessions >= 0),
                "Enabled Baize HTTP diagnostics require a directory, a positive " +
                "body limit, and a non-negative retention limit.")
            .ValidateOnStart();

        services.TryAddTransient<HttpTrafficCaptureHandler>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILlmClientDecorator,
                DiagnosticLoggingLlmClientDecorator>());
        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(HttpDiagnosticsRegistrationMarker)))
        {
            services.AddSingleton<HttpDiagnosticsRegistrationMarker>();
            services.AddHttpClient("llm")
                .AddHttpMessageHandler<HttpTrafficCaptureHandler>();
        }

        return services;
    }

    private sealed class HttpDiagnosticsRegistrationMarker;
}
