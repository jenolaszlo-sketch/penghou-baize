using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Qualification.Planning;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Qualification.Execution;

// Executes one qualification scenario through the real Baize routed adapter.
// The terminal transport is selected by the frozen subject's transport
// profile: a recording fake backend for SyntheticFake, or the pinned real
// sockets handler for LoopbackDevelopment/ReviewedNetwork. In every case the
// request crosses the qualification admission guard immediately before the
// terminal, so all offline verification exercises the exact production
// composition a later live run would use.
internal sealed class CaseExecutor : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly ILlmRouter _router;
    private readonly string _modelId;
    private readonly RecordingTerminalHandler? _recordingTerminal;
    private readonly ISecretProbe _secrets;

    public int TerminalSends => _recordingTerminal?.Sends ?? 0;
    public int SecretReads => _secrets.SecretReads;
    public int SecretLeaseDenials => _secrets.LeaseDenials;

    private CaseExecutor(string modelId, ModelEntry entry, Inventory inventory, IHttpAdmissionGate gate,
        ExecutionFaults faults, HttpMessageHandler? injectedTerminal,
        Func<HttpMessageHandler, HttpMessageHandler>? chainWrapper)
    {
        _modelId = modelId;
        var options = new LlmRoutingOptions
        {
            Models = [new LlmModelOptions { Name = modelId, Endpoints = [entry.Endpoint] }],
            Profiles = inventory.Profiles,
            MaxPendingRequests = 1,
            Retry = new LlmRouterRetryOptions { MaximumAttempts = 1 },
            ProviderModules = [new LlmProviderModuleOptions { Assembly = ProviderAssembly(entry.Endpoint.Provider ?? throw new ExecutionException("endpoint-without-provider")) }]
        };
        var destination = entry.Endpoint.BaseUrl ?? throw new ExecutionException("endpoint-without-base-url");
        var uri = new Uri(destination);
        var boundaryKind = entry.TransportProfile switch
        {
            "SyntheticFake" => "synthetic-terminal-transport",
            "LoopbackDevelopment" => "loopback-terminal",
            _ => "network-terminal"
        };
        var terminal = injectedTerminal ?? CreateTerminal(entry.TransportProfile, out _recordingTerminal);
        if (injectedTerminal is RecordingTerminalHandler injectedRecording) _recordingTerminal = injectedRecording;
        var guard = new QualificationAdmissionHandler(terminal, gate, boundaryKind);
        var chain = chainWrapper is null ? guard : chainWrapper(guard);
        var chainFactory = new GuardedHttpClientFactory(chain, disposeHandler: true);
        _services = Compose(options, entry, chainFactory, out _secrets);
        _router = _services.GetRequiredService<ILlmRouter>();
        _destinationOrigin = uri.GetLeftPart(UriPartial.Authority);
        _destinationBasePath = uri.AbsolutePath.TrimEnd('/');
    }

    private readonly string _destinationOrigin;
    private readonly string _destinationBasePath;
    public string DestinationOrigin => _destinationOrigin;
    public string DestinationBasePath => _destinationBasePath;

    public static CaseExecutor Create(string modelId, ModelEntry entry, Inventory inventory,
        IHttpAdmissionGate gate, ExecutionFaults faults) =>
        new(modelId, entry, inventory, gate, faults, null, null);

    // Offline verification of real-shaped profiles: the terminal is replaced
    // by a controlled handler while everything above the guard stays real.
    internal static CaseExecutor CreateWithTerminal(string modelId, ModelEntry entry, Inventory inventory,
        IHttpAdmissionGate gate, ExecutionFaults faults, HttpMessageHandler terminal) =>
        new(modelId, entry, inventory, gate, faults, terminal, null);

    // Wraps the guard chain (for example with a retry handler above the
    // admission boundary) to prove hidden retries are counted and capped.
    internal static CaseExecutor CreateWithChainWrapper(string modelId, ModelEntry entry, Inventory inventory,
        IHttpAdmissionGate gate, ExecutionFaults faults, HttpMessageHandler terminal,
        Func<HttpMessageHandler, HttpMessageHandler> chainWrapper) =>
        new(modelId, entry, inventory, gate, faults, terminal, chainWrapper);

    private static HttpMessageHandler CreateTerminal(string transportProfile, out RecordingTerminalHandler? recording)
    {
        if (transportProfile == "SyntheticFake")
        {
            recording = new RecordingTerminalHandler();
            return recording;
        }
        recording = null;
        return PinnedTerminalHandlers.CreateRealNetworkTerminal();
    }

    private static string ProviderAssembly(string provider) => provider switch
    {
        "OpenAi" => "Penghou.Baize.OpenAi",
        "Claude" => "Penghou.Baize.Claude",
        "Ollama" => "Penghou.Baize.Ollama",
        "Gemini" => "Penghou.Baize.Gemini",
        _ => throw new ExecutionException("unsupported-provider")
    };

    private static ServiceProvider Compose(LlmRoutingOptions options, ModelEntry entry,
        GuardedHttpClientFactory chainFactory, out ISecretProbe secrets)
    {
        ISecretProbe probe;
        if (entry.TransportProfile == "SyntheticFake")
        {
            probe = new SyntheticSecretProvider(entry.Endpoint.ApiKeySecretName ?? "");
        }
        else
        {
            probe = new LeaseAwareSecretProvider(new EnvironmentSecretProvider());
        }
        secrets = probe;
        var services = new ServiceCollection();
        services.AddSingleton<ISecretProvider>(probe);
        services.AddSingleton<IHttpClientFactory>(chainFactory);
        services.AddSingleton<IOptionsMonitor<LlmRoutingOptions>>(new FixedOptions(options));
        using var json = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            LlmRouting = options
        }));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        services.AddLlmRouting(configuration);
        return services.BuildServiceProvider();
    }

    public IAsyncEnumerable<LlmStreamEvent> Stream(LlmRequest request, CancellationToken cancellationToken) =>
        _router.StreamAsync(_modelId, request, cancellationToken);

    public void Dispose() => _services.Dispose();

    private sealed class FixedOptions(LlmRoutingOptions options) : IOptionsMonitor<LlmRoutingOptions>
    {
        public LlmRoutingOptions CurrentValue => options;
        public LlmRoutingOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<LlmRoutingOptions, string?> listener) => null;
    }
}
