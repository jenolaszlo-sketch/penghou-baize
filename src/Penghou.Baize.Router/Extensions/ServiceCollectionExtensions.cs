using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Penghou.Baize;
using Penghou.Baize.Claude;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Router.Configuration;

namespace Penghou.Baize.Router.Extensions;

/// <summary>Dependency-injection helpers for configuring model routing.</summary>
public static class ServiceCollectionExtensions
{
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

        services.TryAddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        services.TryAddSingleton<ILlmRouterMemory, InMemoryLlmRouterMemory>();

        services.AddSingleton<ILlmModelLookup>(sp =>
            new ReloadingLlmModelLookup(ResolveOptionsMonitor(sp, section), sp));
        services.AddSingleton<IReadOnlyDictionary<ModelStrategy, IReadOnlyList<string>>>(_ =>
            options.StrategyFallbacks.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.AsReadOnly()
            ).AsReadOnly());

        services.AddSingleton<ILlmRouter>(sp =>
        {
            var memory = sp.GetRequiredService<ILlmRouterMemory>();
            var monitor = ResolveOptionsMonitor(sp, section);
            return new ReloadingLlmRouter(monitor, sp, memory);
        });

        return services;
    }

    /// <summary>Builds the model lookup for a set of routing options.</summary>
    /// <param name="sp">The service provider used to resolve clients and secrets.</param>
    /// <param name="options">The routing options to build the lookup from.</param>
    /// <returns>The built lookup.</returns>
    internal static ILlmModelLookup BuildLookup(IServiceProvider sp, LlmRoutingOptions options)
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var secrets = sp.GetRequiredService<ISecretProvider>();
        var defaults = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var byStyle = new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>();
        var stylesByModel = new Dictionary<string, List<ApiStyle>>(StringComparer.Ordinal);
        var byEndpointId = new Dictionary<string, Func<ILlmClient>>(StringComparer.Ordinal);
        var endpointsByModel =
            new Dictionary<string, List<ResolvedEndpoint>>(StringComparer.Ordinal);

        foreach (var model in options.Models)
        {
            var styles = new List<ApiStyle>();
            var endpoints = new List<ResolvedEndpoint>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var endpoint in model.Endpoints)
            {
                var factory = CreateClientFactory(httpClientFactory, secrets, model.Name, endpoint, options.Profiles);
                var id = endpoint.Id ?? $"{model.Name}:{endpoint.ApiStyle}";

                if (!usedIds.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate endpoint id '{id}' for model '{model.Name}'. " +
                        "Give each endpoint a distinct Id.");
                }

                byStyle[(model.Name, endpoint.ApiStyle)] = factory;
                styles.Add(endpoint.ApiStyle);
                byEndpointId[id] = factory;
                endpoints.Add(new ResolvedEndpoint(id, model.Name, endpoint.ApiStyle));

                // The first endpoint registered for a name wins as the
                // plain-name default.
                defaults.TryAdd(model.Name, factory);
            }

            stylesByModel[model.Name] = styles;
            endpointsByModel[model.Name] = endpoints;
        }

        return new LlmModelLookup(
            defaults,
            byStyle,
            stylesByModel.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<ApiStyle>)kv.Value),
            byEndpointId,
            endpointsByModel.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<ResolvedEndpoint>)kv.Value));
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

    private static Func<ILlmClient> CreateClientFactory(
        IHttpClientFactory httpClientFactory,
        ISecretProvider secrets,
        string modelName,
        LlmEndpointOptions endpoint,
        IReadOnlyDictionary<string, LlmEndpointCapabilitiesOptions> profiles)
    {
        var apiKey = ResolveApiKeyAsync(modelName, endpoint, secrets).GetAwaiter().GetResult();
        var providerModel = endpoint.ProviderModel ?? modelName;
        var baseUrl = endpoint.BaseUrl ?? DefaultBaseUrl(endpoint.ApiStyle);
        var capabilities = ResolveCapabilities(endpoint, profiles);

        return endpoint.ApiStyle switch
        {
            ApiStyle.OpenAi => () => new OpenAiChatClient(
                providerModel,
                httpClientFactory,
                apiKey,
                baseUrl,
                capabilities,
                endpoint.Dialect ?? OpenAiDialect.Standard),
            ApiStyle.Claude => () => new ClaudeChatClient(
                httpClientFactory,
                providerModel,
                apiKey,
                baseUrl,
                capabilities,
                endpoint.ThinkingStyle ?? ClaudeThinkingStyle.Adaptive),
            ApiStyle.Ollama => () => new OllamaChatClient(
                providerModel, httpClientFactory, apiKey, baseUrl, capabilities),
            ApiStyle.Gemini => () => new GeminiChatClient(
                providerModel, httpClientFactory, apiKey, baseUrl, capabilities),
            _ => throw new InvalidOperationException(
                $"Unknown API style '{endpoint.ApiStyle}' for model '{modelName}'")
        };
    }

    /// <summary>
    /// Resolves an endpoint's effective capabilities. The API style's
    /// conservative defaults are overlaid first with the named profile
    /// (when one is referenced), then with any per-endpoint overrides.
    /// </summary>
    /// <param name="endpoint">The endpoint to resolve capabilities for.</param>
    /// <param name="profiles">The named capability profiles.</param>
    /// <returns>The effective capabilities.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="endpoint"/> references a profile name that
    /// is not present in <paramref name="profiles"/>.
    /// </exception>
    internal static LlmEndpointCapabilities ResolveCapabilities(
        LlmEndpointOptions endpoint,
        IReadOnlyDictionary<string, LlmEndpointCapabilitiesOptions> profiles)
    {
        var defaults = DefaultCapabilities(endpoint.ApiStyle);
        LlmEndpointCapabilitiesOptions? overrides = endpoint.Capabilities;

        if (endpoint.Profile is not null)
        {
            if (!profiles.TryGetValue(endpoint.Profile, out var profile))
            {
                throw new InvalidOperationException(
                    $"Endpoint for style '{endpoint.ApiStyle}' references unknown " +
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
                    : overrides.ContentTypes.ToHashSet()
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
                top.ContentTypes ?? baseOptions.ContentTypes
        };
    }

    /// <summary>
    /// Returns the conservative default capabilities for an API style: only
    /// what the wire protocol guarantees, not what a particular model might
    /// support. Known models can opt in to more via named profiles (see
    /// <see cref="LlmRoutingOptions.Profiles"/>).
    /// </summary>
    /// <param name="apiStyle">The API style.</param>
    /// <returns>The style's conservative default capabilities.</returns>
    internal static LlmEndpointCapabilities DefaultCapabilities(
        ApiStyle apiStyle) =>
        apiStyle switch
        {
            // The OpenAI-compatible wire protocol guarantees tool definitions
            // and streaming tool-call arguments, but does not guarantee the
            // response_format parameter, reasoning effort, parallel tool
            // calls, or a thinking toggle (those are model/dialect specific).
            // The reasoning_effort tiers low/medium/high are claimed; "max" is
            // not, because it would be silently capped to "high".
            ApiStyle.OpenAi => new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                ParallelToolCalls = false,
                NativeStructuredOutput = false,
                StructuredOutputViaTool = false,
                Thinking = false,
                ThinkingDisable = false,
                StreamingToolCallArguments = true,
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Low,
                        LlmThinkingEffort.Medium,
                        LlmThinkingEffort.High
                    }
            },
            ApiStyle.Claude => new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                ParallelToolCalls = true,
                NativeStructuredOutput = false,
                StructuredOutputViaTool = true,
                Thinking = true,
                ThinkingDisable = false,
                StreamingToolCallArguments = true,
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Low,
                        LlmThinkingEffort.Medium,
                        LlmThinkingEffort.High
                    }
            },
            // Ollama is a local model runner: tool support and structured
            // output depend on the model, not the protocol, so the defaults
            // claim nothing beyond plain text streaming. Opt models in via a
            // profile when they support tools or JSON mode.
            ApiStyle.Ollama => new LlmEndpointCapabilities
            {
                NativeToolCalling = false,
                ParallelToolCalls = false,
                NativeStructuredOutput = false,
                StructuredOutputViaTool = false,
                Thinking = false,
                ThinkingDisable = false,
                StreamingToolCallArguments = false,
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>()
            },
            ApiStyle.Gemini => new LlmEndpointCapabilities
            {
                NativeToolCalling = true,
                ParallelToolCalls = true,
                NativeStructuredOutput = true,
                StructuredOutputViaTool = false,
                Thinking = true,
                ThinkingDisable = false,
                StreamingToolCallArguments = true,
                SupportedThinkingEfforts =
                    new HashSet<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Low,
                        LlmThinkingEffort.Medium,
                        LlmThinkingEffort.High,
                        LlmThinkingEffort.Max
                    }
            },
            _ => throw new InvalidOperationException(
                $"Unknown API style '{apiStyle}'")
        };

    private static string DefaultBaseUrl(ApiStyle apiStyle) =>
        apiStyle switch
        {
            ApiStyle.OpenAi => "https://api.openai.com/v1",
            ApiStyle.Claude => "https://api.anthropic.com",
            ApiStyle.Ollama => "http://localhost:11434",
            ApiStyle.Gemini => "https://generativelanguage.googleapis.com",
            _ => throw new InvalidOperationException($"Unknown API style '{apiStyle}'")
        };

    private static async Task<string> ResolveApiKeyAsync(
        string modelName,
        LlmEndpointOptions endpoint,
        ISecretProvider secrets)
    {
        if (string.IsNullOrEmpty(endpoint.ApiKeyEnvVar))
            return string.Empty; // e.g. local Ollama, no auth

        var value = await secrets.GetSecretAsync(endpoint.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Secret '{endpoint.ApiKeyEnvVar}' is required for model " +
                $"'{modelName}' but was not resolved.");

        return value;
    }

    /// <summary>Validates routing options without throwing.</summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="error">The validation error, when validation fails.</param>
    /// <returns><c>true</c> when the options are valid; otherwise <c>false</c>.</returns>
    internal static bool TryValidate(LlmRoutingOptions options, out string? error)
    {
        var allModelNames = new HashSet<string>(StringComparer.Ordinal);
        var seenEndpointIds = new HashSet<string>(StringComparer.Ordinal);

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
                var id = endpoint.Id ?? $"{model.Name}:{endpoint.ApiStyle}";

                if (!seenEndpointIds.Add(id))
                {
                    error = $"Duplicate endpoint id: '{id}'. Give each endpoint of a model a distinct Id.";
                    return false;
                }

                if (endpoint.Profile is not null &&
                    !options.Profiles.ContainsKey(endpoint.Profile))
                {
                    error =
                        $"Endpoint '{model.Name}' with API style '{endpoint.ApiStyle}' " +
                        $"references unknown capability profile '{endpoint.Profile}'.";
                    return false;
                }
            }
        }

        foreach (var (strategy, chain) in options.StrategyFallbacks)
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

        error = null;
        return true;
    }

    private static void ValidateConfiguration(LlmRoutingOptions options)
    {
        if (!TryValidate(options, out var error))
            throw new InvalidOperationException(error);
    }
}
