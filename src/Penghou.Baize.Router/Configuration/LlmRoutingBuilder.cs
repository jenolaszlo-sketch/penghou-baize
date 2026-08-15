namespace Penghou.Baize.Router.Configuration;

/// <summary>Fluent authoring surface for <see cref="LlmRoutingOptions"/>.</summary>
public sealed class LlmRoutingBuilder
{
    private readonly List<LlmProviderModuleOptions> _modules = [];
    private readonly List<LlmModelOptions> _models = [];
    private readonly Dictionary<string, LlmEndpointCapabilitiesOptions> _profiles =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ModelStrategy, List<string>> _strategies = [];
    private readonly Dictionary<string, List<string>> _routes =
        new(StringComparer.Ordinal);
    private int _maxPendingRequests;
    private TimeSpan? _requestTimeout;
    private LlmRouterRetryOptions _retry = new();

    internal bool ValidateEndpointsAtStartup { get; private set; }

    /// <summary>Adds a trusted provider module assembly by simple or display name.</summary>
    public LlmRoutingBuilder AddProviderModule(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        _modules.Add(new LlmProviderModuleOptions { Assembly = assemblyName });
        return this;
    }

    /// <summary>Adds a logical model and its endpoints.</summary>
    public LlmRoutingBuilder AddModel(string name, Action<LlmModelBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LlmModelBuilder(name);
        configure(builder);
        _models.Add(builder.Build());
        return this;
    }

    /// <summary>Adds a named capability profile.</summary>
    public LlmRoutingBuilder AddProfile(
        string name,
        Action<LlmEndpointCapabilitiesBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LlmEndpointCapabilitiesBuilder();
        configure(builder);
        _profiles.Add(name, builder.Build());
        return this;
    }

    /// <summary>Defines the fallback chain for a built-in strategy.</summary>
    public LlmRoutingBuilder AddStrategy(ModelStrategy strategy, params string[] models)
    {
        _strategies.Add(strategy, ValidateChain(models));
        return this;
    }

    /// <summary>Defines an application-specific named route.</summary>
    public LlmRoutingBuilder AddNamedRoute(string name, params string[] models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _routes.Add(name, ValidateChain(models));
        return this;
    }

    /// <summary>Bounds concurrent in-flight requests; zero means unbounded.</summary>
    public LlmRoutingBuilder WithMaxPendingRequests(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        _maxPendingRequests = maximum;
        return this;
    }

    /// <summary>Applies a timeout to each routed request.</summary>
    public LlmRoutingBuilder WithRequestTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _requestTimeout = timeout;
        return this;
    }

    /// <summary>Configures bounded retries after transient route exhaustion.</summary>
    public LlmRoutingBuilder WithTransientRetries(
        int maximumAttempts,
        TimeSpan initialDelay,
        double backoffFactor = 2,
        TimeSpan? maximumDelay = null)
    {
        var retry = new LlmRouterRetryOptions
        {
            MaximumAttempts = maximumAttempts,
            InitialDelay = initialDelay,
            BackoffFactor = backoffFactor,
            MaximumDelay = maximumDelay ?? TimeSpan.FromSeconds(30)
        };
        retry.Validate();
        _retry = retry;
        return this;
    }

    /// <summary>Resolves secrets and constructs every provider client when the host starts.</summary>
    public LlmRoutingBuilder ValidateEndpointsOnStart()
    {
        ValidateEndpointsAtStartup = true;
        return this;
    }

    internal LlmRoutingOptions Build() => new()
    {
        ProviderModules = _modules,
        Models = _models,
        Profiles = _profiles,
        StrategyFallbacks = _strategies,
        NamedRoutes = _routes,
        MaxPendingRequests = _maxPendingRequests,
        RequestTimeout = _requestTimeout,
        Retry = _retry
    };

    private static List<string> ValidateChain(string[] models)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (models.Length == 0 || models.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A route must contain at least one non-empty model name.", nameof(models));
        return [.. models];
    }
}
