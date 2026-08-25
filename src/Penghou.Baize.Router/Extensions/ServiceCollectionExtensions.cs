using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Penghou.Baize;
using Penghou.Baize.Generation;
using Penghou.Baize.Router.Configuration;

namespace Penghou.Baize.Router.Extensions;

/// <summary>Dependency-injection helpers for configuring model routing.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers routing services from a fluent configuration.</summary>
    public static IServiceCollection AddLlmRouting(
        this IServiceCollection services,
        Action<LlmRoutingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LlmRoutingBuilder();
        configure(builder);
        var options = builder.Build();
        ValidateConfiguration(options);
        RegisterRoutingServices(
            services,
            options,
            _ => new StaticOptionsMonitor<LlmRoutingOptions>(options));

        if (builder.ValidateEndpointsAtStartup)
            services.AddLlmEndpointValidationOnStart();

        return services;
    }

    /// <summary>
    /// Validates provider construction and secret resolution when the host
    /// starts. This can also be used with configuration-file registration.
    /// </summary>
    public static IServiceCollection AddLlmEndpointValidationOnStart(
        this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService,
            LlmEndpointValidationHostedService>());
        return services;
    }

    /// <summary>
    /// Registers routing services from the <c>LlmRouting</c> configuration
    /// section: the model lookup, the strategy lookup, an
    /// <see cref="ILlmRouterMemory"/> (defaulting to
    /// <see cref="InMemoryLlmRouterMemory"/>), an
    /// <see cref="ISecretProvider"/> (defaulting to
    /// <see cref="EnvironmentSecretProvider"/>), and an
    /// <see cref="ILlmRouter"/>. The router and the model lookup are rebuilt
    /// whenever the options change; when the application registers its own
    /// <see cref="IOptionsMonitor{LlmRoutingOptions}"/> it is used, otherwise
    /// a monitor bound to the <c>LlmRouting</c> section reloads from
    /// configuration. The memory and secret provider defaults are registered
    /// with <c>TryAdd</c>, so applications can register their own afterwards
    /// to replace them.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="config">The application configuration containing the <c>LlmRouting</c> section.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <c>LlmRouting</c> section is missing or invalid
    /// (duplicate models, models without endpoints, duplicate endpoints, or
    /// unknown fallback references).
    /// </exception>
    public static IServiceCollection AddLlmRouting(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("LlmRouting");
        var options = section.Get<LlmRoutingOptions>()
            ?? throw new InvalidOperationException("Missing 'LlmRouting' configuration section.");

        ValidateConfiguration(options);

        RegisterRoutingServices(
            services,
            options,
            sp => ResolveOptionsMonitor(sp, section));

        return services;
    }

    private static void RegisterRoutingServices(
        IServiceCollection services,
        LlmRoutingOptions options,
        Func<IServiceProvider, IOptionsMonitor<LlmRoutingOptions>> monitorFactory)
    {
        ProviderModuleLoader.Register(services, options.ProviderModules);
        services.TryAddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        services.TryAddSingleton<ILlmRouterMemory, InMemoryLlmRouterMemory>();
        services.TryAddSingleton<ILlmEndpointSelectionPolicy,
            ReliabilityEndpointSelectionPolicy>();
        services.TryAddSingleton<IGenerationEndpointOrderer,
            RouterGenerationEndpointOrderer>();
        services.TryAddSingleton<ILlmClientProviderRegistry, LlmClientProviderRegistry>();

        services.AddSingleton(sp => new ReloadingLlmRoutingState(
            monitorFactory(sp),
            sp,
            sp.GetRequiredService<ILlmRouterMemory>(),
            sp.GetRequiredService<ILlmEndpointSelectionPolicy>(),
            sp.GetService<ILogger<ReloadingLlmRoutingState>>()));
        services.AddSingleton<ILlmModelLookup>(sp =>
            sp.GetRequiredService<ReloadingLlmRoutingState>());
        services.AddSingleton<ILlmRouter>(sp =>
            sp.GetRequiredService<ReloadingLlmRoutingState>());
        services.AddSingleton<ILlmEndpointValidator>(sp =>
            sp.GetRequiredService<ReloadingLlmRoutingState>());

    }

    /// <summary>Builds the model lookup for a set of routing options.</summary>
    /// <param name="sp">The service provider used to resolve clients and secrets.</param>
    /// <param name="options">The routing options to build the lookup from.</param>
    /// <returns>The built lookup.</returns>
    internal static ILlmModelLookup BuildLookup(IServiceProvider sp, LlmRoutingOptions options)
        => BuildRoutingLookup(sp, options).Lookup;

    internal static BuiltRoutingLookup BuildRoutingLookup(
        IServiceProvider sp,
        LlmRoutingOptions options)
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var secrets = sp.GetRequiredService<ISecretProvider>();
        var providers = sp.GetService<ILlmClientProviderRegistry>()
            ?? new LlmClientProviderRegistry(
                sp.GetService<IEnumerable<ILlmClientProvider>>() ?? []);
        var decorators =
            sp.GetService<IEnumerable<ILlmClientDecorator>>()?.ToArray() ?? [];
        var providerLogger = sp.GetService<ILoggerFactory>()?
            .CreateLogger("Penghou.Baize.Router.Provider");
        var defaults = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var byProvider =
            new Dictionary<(string Model, LlmProviderKey Provider), Func<ILlmClient>>();
        var providersByModel =
            new Dictionary<string, List<LlmProviderKey>>(StringComparer.Ordinal);
        var byEndpointId = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var batchByEndpointId =
            new Dictionary<string, Func<IBaizeBatchClient>>(StringComparer.Ordinal);
        var endpointsByModel =
            new Dictionary<string, List<ResolvedEndpoint>>(StringComparer.Ordinal);
        var runtimeEndpoints = new List<DeferredEndpointRuntime>();

        foreach (var model in options.Models)
        {
            var providerKeys = new List<LlmProviderKey>();
            var endpoints = new List<ResolvedEndpoint>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var endpoint in model.Endpoints)
            {
                var provider = providers.GetRequiredProvider(endpoint.ProviderKey);
                var id = endpoint.Id ?? $"{model.Name}:{endpoint.ProviderKey}";
                var providerModel = endpoint.ProviderModel ?? model.Name;
                var baseUrl = endpoint.BaseUrl ?? provider.DefaultBaseUrl;
                var capabilities = ResolveCapabilities(
                    endpoint,
                    options.Profiles,
                    provider);
                var deferred = new DeferredEndpointClients(
                    provider,
                    id,
                    providerModel,
                    providerLogger,
                    () => CreateProviderContextAsync(
                        httpClientFactory,
                        secrets,
                        provider,
                        model.Name,
                        endpoint,
                        capabilities));
                ILlmClient client = new DeferredLlmClient(
                    deferred,
                    capabilities,
                    new LlmClientMetadata(
                        provider.Key.Value,
                        providerModel,
                        Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpointUri)
                            ? endpointUri
                            : null,
                        id));

                foreach (var decorator in decorators)
                {
                    client = decorator.Decorate(client) ??
                        throw new InvalidOperationException(
                            $"LLM client decorator '{decorator.GetType().FullName}' " +
                            "returned null.");
                }

                Func<ILlmClient> factory = () => client;

                if (!usedIds.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate endpoint id '{id}' for model '{model.Name}'. " +
                        "Give each endpoint a distinct Id.");
                }

                // The (model, provider) accessor returns the first matching
                // endpoint; later endpoints of the same provider are reached by
                // their id, mirroring the plain-name default.
                byProvider.TryAdd((model.Name, endpoint.ProviderKey), factory);
                providerKeys.Add(endpoint.ProviderKey);
                byEndpointId[id] = factory;

                if (capabilities.Batch.HasFlag(BatchCapabilities.NativeBatch))
                {
                    var batchClient = new EndpointBoundBatchClient(
                        id,
                        new DeferredBatchClient(deferred, capabilities.Batch));
                    batchByEndpointId[id] = () => batchClient;
                }
                endpoints.Add(new ResolvedEndpoint(id, model.Name, endpoint.ProviderKey));
                runtimeEndpoints.Add(new DeferredEndpointRuntime(
                    id,
                    provider.Key.Value,
                    providerModel,
                    deferred,
                    capabilities.Batch.HasFlag(BatchCapabilities.NativeBatch)));

                // The first endpoint registered for a name wins as the
                // plain-name default.
                defaults.TryAdd(model.Name, factory);
            }

            providersByModel[model.Name] = providerKeys;
            endpointsByModel[model.Name] = endpoints;
        }

        return new BuiltRoutingLookup(
            new LlmModelLookup(
                defaults,
                byProvider,
                providersByModel.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<LlmProviderKey>)kv.Value),
                byEndpointId,
                endpointsByModel.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<ResolvedEndpoint>)kv.Value),
                batchByEndpointId),
            runtimeEndpoints);
    }

    /// <summary>
    /// Prefers an application-registered <see cref="IOptionsMonitor{LlmRoutingOptions}"/>
    /// when it holds configured models; otherwise returns a monitor bound to
    /// the <c>LlmRouting</c> section that reloads from configuration.
    /// </summary>
    private static IOptionsMonitor<LlmRoutingOptions> ResolveOptionsMonitor(
        IServiceProvider services,
        IConfigurationSection section)
    {
        var appMonitor = services.GetService<IOptionsMonitor<LlmRoutingOptions>>();
        if (appMonitor is not null && appMonitor.CurrentValue.Models.Count > 0)
            return appMonitor;

        return new ConfigurationOptionsMonitor<LlmRoutingOptions>(
            section,
            (options, _) => TryValidate(options, out _));
    }

    private static async Task<LlmClientProviderContext> CreateProviderContextAsync(
        IHttpClientFactory httpClientFactory,
        ISecretProvider secrets,
        ILlmClientProvider provider,
        string modelName,
        LlmEndpointOptions endpoint,
        LlmEndpointCapabilities capabilities)
    {
        var apiKey = await ResolveApiKeyAsync(modelName, endpoint, secrets);
        var providerModel = endpoint.ProviderModel ?? modelName;
        var baseUrl = endpoint.BaseUrl ?? provider.DefaultBaseUrl;
        var settings = ResolveProviderSettings(endpoint);

        // Per-model request timeout: wrap the shared factory so every client
        // this endpoint creates (chat and batch alike) enforces it.
        var effectiveFactory = endpoint.RequestTimeout is { } requestTimeout
            ? httpClientFactory.WithRequestTimeout(requestTimeout)
            : httpClientFactory;

        return new LlmClientProviderContext(
            providerModel,
            effectiveFactory,
            apiKey,
            baseUrl,
            capabilities,
            settings);
    }

    /// <summary>
    /// Resolves an endpoint's effective capabilities. The provider's
    /// conservative defaults are overlaid first with the named profile
    /// (when one is referenced), then with any per-endpoint overrides.
    /// </summary>
    /// <param name="endpoint">The endpoint to resolve capabilities for.</param>
    /// <param name="profiles">The named capability profiles.</param>
    /// <param name="provider">The provider supplying conservative defaults.</param>
    /// <returns>The effective capabilities.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="endpoint"/> references a profile name that
    /// is not present in <paramref name="profiles"/>.
    /// </exception>
    internal static LlmEndpointCapabilities ResolveCapabilities(
        LlmEndpointOptions endpoint,
        IReadOnlyDictionary<string, LlmEndpointCapabilitiesOptions> profiles,
        ILlmClientProvider provider)
    {
        var defaults = provider.DefaultCapabilities;
        LlmEndpointCapabilitiesOptions? overrides = endpoint.Capabilities;

        if (endpoint.Profile is not null)
        {
            if (!profiles.TryGetValue(endpoint.Profile, out var profile))
            {
                throw new InvalidOperationException(
                    $"Endpoint for provider '{endpoint.ProviderKey}' references unknown " +
                    $"capability profile '{endpoint.Profile}'.");
            }

            overrides = Overlay(profile, overrides);
        }

        if (overrides is null)
        {
            return defaults;
        }

        return defaults with
        {
            NativeToolCalling =
                overrides.NativeToolCalling ?? defaults.NativeToolCalling,
            ParallelToolCalls =
                overrides.ParallelToolCalls ?? defaults.ParallelToolCalls,
            StrictToolArguments =
                overrides.StrictToolArguments ?? defaults.StrictToolArguments,
            ToolsWithStructuredOutput =
                overrides.ToolsWithStructuredOutput ?? defaults.ToolsWithStructuredOutput,
            NativeStructuredOutput =
                overrides.NativeStructuredOutput ?? defaults.NativeStructuredOutput,
            StructuredOutputViaTool =
                overrides.StructuredOutputViaTool ?? defaults.StructuredOutputViaTool,
            Thinking =
                overrides.Thinking ?? defaults.Thinking,
            ThinkingDisable =
                overrides.ThinkingDisable ?? defaults.ThinkingDisable,
            StreamingToolCallArguments =
                overrides.StreamingToolCallArguments ?? defaults.StreamingToolCallArguments,
            SupportedThinkingEfforts =
                overrides.SupportedThinkingEfforts is null
                    ? defaults.SupportedThinkingEfforts
                    : overrides.SupportedThinkingEfforts.ToHashSet(),
            ThinkingBudget =
                overrides.ThinkingBudget ?? defaults.ThinkingBudget,
            ContentTypes =
                overrides.ContentTypes is null
                    ? defaults.ContentTypes
                    : overrides.ContentTypes.ToHashSet(),
            ContentTransports =
                overrides.ContentTransports is null
                    ? defaults.ContentTransports
                    : new Dictionary<LlmContentType, LlmContentTransport>(
                        overrides.ContentTransports),
            Batch =
                overrides.Batch ?? defaults.Batch
        };
    }

    /// <summary>
    /// Overlays <paramref name="top"/> on <paramref name="baseOptions"/>,
    /// where non-null members of the former win over the latter.
    /// </summary>
    private static LlmEndpointCapabilitiesOptions? Overlay(
        LlmEndpointCapabilitiesOptions? baseOptions,
        LlmEndpointCapabilitiesOptions? top)
    {
        if (baseOptions is null)
            return top;

        if (top is null)
            return baseOptions;

        return new LlmEndpointCapabilitiesOptions
        {
            NativeToolCalling =
                top.NativeToolCalling ?? baseOptions.NativeToolCalling,
            ParallelToolCalls =
                top.ParallelToolCalls ?? baseOptions.ParallelToolCalls,
            StrictToolArguments =
                top.StrictToolArguments ?? baseOptions.StrictToolArguments,
            ToolsWithStructuredOutput =
                top.ToolsWithStructuredOutput ?? baseOptions.ToolsWithStructuredOutput,
            NativeStructuredOutput =
                top.NativeStructuredOutput ?? baseOptions.NativeStructuredOutput,
            StructuredOutputViaTool =
                top.StructuredOutputViaTool ?? baseOptions.StructuredOutputViaTool,
            Thinking =
                top.Thinking ?? baseOptions.Thinking,
            ThinkingDisable =
                top.ThinkingDisable ?? baseOptions.ThinkingDisable,
            StreamingToolCallArguments =
                top.StreamingToolCallArguments ?? baseOptions.StreamingToolCallArguments,
            SupportedThinkingEfforts =
                top.SupportedThinkingEfforts ?? baseOptions.SupportedThinkingEfforts,
            ThinkingBudget =
                top.ThinkingBudget ?? baseOptions.ThinkingBudget,
            ContentTypes =
                top.ContentTypes ?? baseOptions.ContentTypes,
            ContentTransports =
                top.ContentTransports ?? baseOptions.ContentTransports,
            Batch =
                top.Batch ?? baseOptions.Batch
        };
    }

    private static IReadOnlyDictionary<string, string> ResolveProviderSettings(
        LlmEndpointOptions endpoint)
    {
        var settings = new Dictionary<string, string>(
            endpoint.Settings,
            StringComparer.OrdinalIgnoreCase);

        if (endpoint.Dialect is not null)
            settings.TryAdd(LlmSettingNames.Dialect, endpoint.Dialect);

        if (endpoint.ThinkingStyle is not null)
            settings.TryAdd(LlmSettingNames.ThinkingStyle, endpoint.ThinkingStyle);

        return settings;
    }

    private static async Task<string> ResolveApiKeyAsync(
        string modelName,
        LlmEndpointOptions endpoint,
        ISecretProvider secrets)
    {
        if (string.IsNullOrEmpty(endpoint.ApiKeySecretName))
            return string.Empty; // e.g. local Ollama, no auth

        var value = await secrets.GetSecretAsync(endpoint.ApiKeySecretName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Secret '{endpoint.ApiKeySecretName}' is required for model " +
                $"'{modelName}' but was not resolved.");

        return value;
    }

    /// <summary>Validates routing options without throwing.</summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="error">The validation error, when validation fails.</param>
    /// <returns><c>true</c> when the options are valid; otherwise <c>false</c>.</returns>
    internal static bool TryValidate(LlmRoutingOptions options, out string? error)
    {
        if (options.Retry is null)
        {
            error = "Retry configuration is required.";
            return false;
        }

        try
        {
            options.Retry.Validate();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            error = $"Retry configuration is invalid: {exception.Message}";
            return false;
        }

        var allModelNames = new HashSet<string>(StringComparer.Ordinal);
        var seenEndpointIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in options.ProviderModules)
        {
            if (string.IsNullOrWhiteSpace(module.Assembly))
            {
                error = "ProviderModules contains an entry without an assembly name.";
                return false;
            }

            if (ProviderModuleLoader.IsAssemblyPath(module.Assembly))
            {
                error =
                    $"Provider module '{module.Assembly}' must be an assembly name, not a path.";
                return false;
            }
        }

        foreach (var model in options.Models)
        {
            if (!allModelNames.Add(model.Name))
            {
                error = $"Duplicate model registration: '{model.Name}'";
                return false;
            }

            if (model.Endpoints.Count == 0)
            {
                error = $"Model '{model.Name}' must declare at least one endpoint.";
                return false;
            }

            foreach (var endpoint in model.Endpoints)
            {
                LlmProviderKey provider;
                try
                {
                    provider = endpoint.ProviderKey;
                }
                catch (ArgumentException exception)
                {
                    error = $"Model '{model.Name}' has an invalid provider: {exception.Message}";
                    return false;
                }

                var id = endpoint.Id ?? $"{model.Name}:{provider}";

                if (!seenEndpointIds.Add(id))
                {
                    error = $"Duplicate endpoint id: '{id}'. Give each endpoint of a model a distinct Id.";
                    return false;
                }

                if (endpoint.Profile is not null &&
                    !options.Profiles.ContainsKey(endpoint.Profile))
                {
                    error =
                        $"Endpoint '{model.Name}' with provider '{provider}' " +
                        $"references unknown capability profile '{endpoint.Profile}'.";
                    return false;
                }

                if (endpoint.RequestTimeout is { } timeout &&
                    timeout <= TimeSpan.Zero)
                {
                    error =
                        $"Endpoint '{model.Name}' with provider '{provider}' " +
                        "has a RequestTimeout that must be positive.";
                    return false;
                }
            }
        }

        foreach (var (strategy, chain) in options.StrategyFallbacks)
        {
            if (chain.Count == 0)
            {
                error = $"StrategyFallbacks['{strategy}'] must contain at least one model.";
                return false;
            }

            foreach (var modelName in chain)
            {
                if (!allModelNames.Contains(modelName))
                {
                    error =
                        $"StrategyFallbacks['{strategy}'] references unknown model '{modelName}'. " +
                        "Check for a typo against the Models[].Name entries.";
                    return false;
                }
            }
        }

        foreach (var (route, chain) in options.NamedRoutes)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                error = "NamedRoutes contains an empty route name.";
                return false;
            }

            if (chain.Count == 0)
            {
                error = $"NamedRoutes['{route}'] must contain at least one model.";
                return false;
            }

            foreach (var modelName in chain)
            {
                if (!allModelNames.Contains(modelName))
                {
                    error =
                        $"NamedRoutes['{route}'] references unknown model '{modelName}'. " +
                        "Check for a typo against the Models[].Name entries.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    internal static void ValidateConfiguration(LlmRoutingOptions options)
    {
        if (!TryValidate(options, out var error))
            throw new LlmConfigurationException(
                LlmConfigurationFailureKind.Structural,
                error!);
    }
}
