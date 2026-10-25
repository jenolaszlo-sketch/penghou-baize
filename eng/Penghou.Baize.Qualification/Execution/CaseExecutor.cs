using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Penghou.Baize.Qualification.Planning;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.Qualification.Execution;

// Executes one qualification scenario through the real Baize routed adapter
// with a deterministic synthetic terminal transport. The adapter, router,
// stream parser and telemetry are the actual production path; nothing here
// bypasses the qualification seam. The eventual real-model stage swaps only
// the terminal transport and secret source behind the same composition.
internal sealed class CaseExecutor : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly ILlmRouter _router;
    private readonly string _modelId;
    private readonly SyntheticTransportHandler _handler;
    private readonly SyntheticSecretProvider _secrets;

    public int TerminalSends => _handler.Sends;
    public int SecretReads => _secrets.SecretReads;
    public string? LastRequestJson => _handler.LastRequestJson;

    private CaseExecutor(string modelId, ModelEntry entry, Inventory inventory)
    {
        _modelId = modelId;
        var options = new LlmRoutingOptions
        {
            Models = [new LlmModelOptions { Name = modelId, Endpoints = [entry.Endpoint] }],
            Profiles = inventory.Profiles,
            MaxPendingRequests = 1,
            Retry = new LlmRouterRetryOptions { MaximumAttempts = 1 },
            ProviderModules = [new LlmProviderModuleOptions { Assembly = "Penghou.Baize.OpenAi" }]
        };
        _services = Compose(options, entry.Endpoint.ApiKeySecretName);
        _router = _services.GetRequiredService<ILlmRouter>();
        _handler = (SyntheticTransportHandler)_services.GetRequiredService<SyntheticTransportHandler>();
        _secrets = (SyntheticSecretProvider)_services.GetRequiredService<ISecretProvider>();
    }

    public static CaseExecutor Create(string modelId, ModelEntry entry, Inventory inventory) =>
        new(modelId, entry, inventory);

    private static ServiceProvider Compose(LlmRoutingOptions options, string? secretSlot)
    {
        var handler = new SyntheticTransportHandler();
        var secrets = new SyntheticSecretProvider(secretSlot ?? "");
        var services = new ServiceCollection();
        services.AddSingleton<SyntheticTransportHandler>(handler);
        services.AddSingleton<ISecretProvider>(secrets);
        services.AddSingleton<IHttpClientFactory>(new SyntheticHttpClientFactory(handler));
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

// Maps the contract's content-free result into evidence records. It never
// touches raw response text: only assertion ids/statuses/codes, counts and
// bounded diagnostics leave this mapping.
internal static class EvidenceMapping
{
    public static AssertionEvidence[] Assertions(QualificationResultV3 result, CaseExecutionRequest request)
    {
        // Assertion facts come from the accepted contract; the request's
        // assertion list was verified against the registry at binding time.
        var observed = new Dictionary<string, AssertionEvidence>(StringComparer.Ordinal);
        foreach (var contract in result.Contracts)
        {
            foreach (var assertion in contract.Assertions)
            {
                if (request.AssertionIds.Contains(assertion.Id, StringComparer.Ordinal)
                    && request.ObservedContractIds.Contains(contract.ContractId, StringComparer.Ordinal))
                    observed[assertion.Id] = new AssertionEvidence(assertion.Id, assertion.Status.ToString(), assertion.Code);
            }
        }
        return request.AssertionIds.Select(id => observed.TryGetValue(id, out var evidence)
            ? evidence : new AssertionEvidence(id, "NotObserved", "execution-incomplete")).ToArray();
    }

    public static AttemptOutcome Outcome(QualificationResultV3 result) => result.Succeeded ? AttemptOutcome.Passed
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Failed) ? AttemptOutcome.Failed
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Cancelled) ? AttemptOutcome.Cancelled
        : result.Contracts.Any(c => c.Disposition == ContractDisposition.Blocked) ? AttemptOutcome.Blocked
        : AttemptOutcome.Inconclusive;

    public static string? FailureCategory(QualificationResultV3 result)
    {
        var diagnostics = result.Diagnostics;
        return diagnostics.FailureCode switch
        {
            "provider-unavailable" => "provider-unavailable",
            "rate-limited" => "rate-limited",
            "authentication" => "authentication",
            "authorization" => "authorization",
            "timeout" => "timeout",
            "caller-cancelled" or "ambiguous-cancellation" => "timeout-or-cancellation",
            "request-validation" or "invalid-request" or "protocol-error" or "content-rejected" => "baize-protocol",
            "execution-error" => "inconclusive",
            _ => null
        };
    }

    public static string? Attribution(AttemptOutcome outcome, QualificationResultV3 result) => outcome switch
    {
        AttemptOutcome.Passed => null,
        AttemptOutcome.Failed => "contract-assertion-failure",
        AttemptOutcome.Blocked or AttemptOutcome.Cancelled or AttemptOutcome.Inconclusive => "not-attributed-infrastructure-or-incomplete",
        _ => null
    };

    public static string AttributionBasis(QualificationResultV3 result) =>
        "observed-normalized-output";

    public static string[] FailedAssertions(AssertionEvidence[] assertions) =>
        assertions.Where(a => a.Status == "Failed").Select(a => a.Id).ToArray();

    public static string Completeness(AssertionEvidence[] assertions) =>
        assertions.Any(a => a.Status == "NotObserved") ? "Incomplete" : "Complete";

    public static StreamDiagnosticsEvidence Diagnostics(QualificationResultV3 result) =>
        new(result.Diagnostics.InvocationCount, result.Diagnostics.EventCount, result.Diagnostics.ContentCharacters,
            result.Diagnostics.ReasoningCharacters, result.Diagnostics.ToolArgumentCharacters,
            result.Diagnostics.TerminalCount, result.Diagnostics.FinishReason.ToString(),
            result.Diagnostics.RouterAttemptCount, result.Diagnostics.ProviderDiagnosticsObserved,
            result.Diagnostics.FailureCode, result.Diagnostics.HttpStatusCode, result.Diagnostics.Integrity.Count);

    public static RouterEvidenceRecord Router(QualificationResultV3 result) =>
        new(result.Router.SnapshotCount, result.Router.AttemptCount, result.Router.SuccessfulAttempts,
            result.Router.FailedAttempts, result.Router.TerminalOrderValid);

    public static UsageEvidence Usage(QualificationResultV3 result)
    {
        var diagnostics = result.Diagnostics;
        var known = diagnostics.PromptTokens is not null && diagnostics.CompletionTokens is not null;
        return new UsageEvidence(diagnostics.PromptTokens, diagnostics.CompletionTokens, diagnostics.ThinkingTokens,
            null, known ? "Complete" : "Unknown", known ? "synthetic-terminal-transport" : "unavailable");
    }
}
