using Penghou.Baize.Qualification.Planning;
using Penghou.Zhinu;
using Penghou.Zhinu.Sqlite;

namespace Penghou.Baize.Qualification.Execution;

// Owns the durable components of one run directory: the Zhinu store/engine
// (workflow lifecycle) and the Siming ledger (evidence). Executors are pooled
// per subject so distinct subjects can never share configuration or counters.
internal sealed class ExecutionEnvironment : IAsyncDisposable
{
    private readonly Inventory _inventory;
    private readonly Dictionary<string, CaseExecutor> _executors = new(StringComparer.Ordinal);
    private readonly InvokerSlot _invoker;

    public EvidenceLedger Ledger { get; }
    public WorkflowEngine Engine { get; }
    public ExecutionFaults Faults { get; }
    public TimeProvider Time { get; }

    private ExecutionEnvironment(EvidenceLedger ledger, WorkflowEngine engine, ExecutionFaults faults,
        TimeProvider time, Inventory inventory, InvokerSlot invoker)
    {
        Ledger = ledger;
        Engine = engine;
        Faults = faults;
        Time = time;
        _inventory = inventory;
        _invoker = invoker;
    }

    public static async Task<ExecutionEnvironment> CreateAsync(string runDirectory, ExecutionFaults faults,
        Inventory inventory, CancellationToken cancellationToken)
    {
        var time = TimeProvider.System;
        var ledger = new EvidenceLedger(Path.Combine(runDirectory, "evidence.db"), faults);
        var store = new SqliteWorkflowStore(new ZhinuSqliteOptions
        {
            DatabasePath = Path.Combine(runDirectory, "workflow.db"),
            TimeProvider = time
        });
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var invoker = new InvokerSlot();
        var workflow = new QualificationWorkflow(new WorkflowServices(ledger, invoker, faults, time));
        var registry = new WorkflowRegistry().Register<CaseExecutionRequest, TrialOutcomeRecord>(
            QualificationWorkflow.Name, QualificationWorkflow.Version, workflow);
        var engine = new WorkflowEngine(store, registry, new ZhinuOptions
        {
            MaxConcurrentWorkflows = 1,
            PollInterval = TimeSpan.FromMilliseconds(25),
            LeaseDuration = TimeSpan.FromSeconds(30),
            LeaseRenewalInterval = TimeSpan.FromSeconds(10)
        }, timeProvider: time);
        var environment = new ExecutionEnvironment(ledger, engine, faults, time, inventory, invoker);
        invoker.Inner = new PooledInvoker(environment);
        workflow.Probe = (runId, token) => engine.GetRunAsync(runId, token);
        return environment;
    }

    public CaseExecutor ExecutorFor(string modelId, ModelEntry entry) =>
        _executors.TryGetValue(modelId, out var executor) ? executor : _executors[modelId] = CaseExecutor.Create(modelId, entry, _inventory);

    public async ValueTask DisposeAsync()
    {
        foreach (var executor in _executors.Values) executor.Dispose();
        _executors.Clear();
        await Engine.DisposeAsync().ConfigureAwait(false);
        await Ledger.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class InvokerSlot : ICaseInvoker
    {
        public ICaseInvoker? Inner { get; set; }
        public Task<QualificationResultV3> InvokeAsync(CaseExecutionRequest request, CancellationToken cancellationToken,
            CancellationToken deadlineToken) =>
            Inner is null ? throw new ExecutionException("invoker-not-wired") : Inner.InvokeAsync(request, cancellationToken, deadlineToken);
    }

    private sealed class PooledInvoker(ExecutionEnvironment environment) : ICaseInvoker
    {
        public Task<QualificationResultV3> InvokeAsync(CaseExecutionRequest request, CancellationToken cancellationToken,
            CancellationToken deadlineToken)
        {
            var entry = environment._inventory.Entries.Single(e => e.Id == request.ModelId);
            var executor = environment.ExecutorFor(request.ModelId, entry);
            var context = new QualificationContextV3(
                (streamRequest, token) => executor.Stream(streamRequest, token),
                NativeToolCalling: request.Subject.NativeToolCalling,
                RequireRouterDiagnostics: request.RequiresRouterDiagnostics,
                OutputBudget: OutputBudgetSupport.Unknown,
                IdentityProtocol: IdentityProtocols.Parse(request.Subject.IdentityProtocol),
                Deadline: deadlineToken);
            return request.RequiresNativeTools
                ? QualificationScenariosV3.RunSingleToolAsync(context, cancellationToken)
                : QualificationScenariosV3.RunBaselineAsync(context, cancellationToken);
        }
    }
}

internal static class IdentityProtocols
{
    public static ToolIdentityProtocol Parse(string protocol) => protocol switch
    {
        "openai-chat-completions" => ToolIdentityProtocol.OpenAiChatCompletions,
        "anthropic-messages" => ToolIdentityProtocol.AnthropicMessages,
        "ollama-chat" => ToolIdentityProtocol.OllamaChat,
        "gemini-generate-content" => ToolIdentityProtocol.GeminiGenerateContent,
        _ => ToolIdentityProtocol.Unspecified
    };
}
