namespace Penghou.Baize.Qualification.Execution;

// Content-free, typed qualification evidence. Raw prompts, responses, tool
// arguments, credentials, exception text and private URLs never enter these
// records; only counts, statuses, hashes, opaque IDs and safe references do.

internal enum AttemptOutcome { Passed, Failed, Blocked, Cancelled, Inconclusive, NotRun }

internal enum EvidenceCompleteness { Complete, Incomplete, Unknown }

internal sealed record AssertionEvidence(string Id, string Status, string Code);

internal sealed record UsageEvidence(
    long? PromptTokens,
    long? CompletionTokens,
    long? ThinkingTokens,
    long? TotalTokens,
    string Completeness,
    string Source,
    string? ObservedCost = null,
    string? CostCompleteness = null);

internal sealed record StreamDiagnosticsEvidence(
    int InvocationCount,
    int EventCount,
    int ContentCharacters,
    int ReasoningCharacters,
    int ToolArgumentCharacters,
    int TerminalCount,
    string FinishReason,
    int? RouterAttemptCount,
    bool ProviderDiagnosticsObserved,
    string? FailureCode,
    int? HttpStatusCode,
    int IntegrityObservationCount);

internal sealed record RouterEvidenceRecord(
    int SnapshotCount,
    int? AttemptCount,
    int? SuccessfulAttempts,
    int? FailedAttempts,
    bool TerminalOrderValid);

internal sealed record SelectedCaseEvidence(
    string CaseId,
    string ModelId,
    string ScenarioId,
    string[] ObservedContractIds,
    int TrialSlot,
    string TrialId);

internal sealed record RunStartedEvidence(
    string SchemaVersion,
    string RunId,
    string PlanHash,
    string ContractRegistryHash,
    string InventoryHash,
    string PolicyHash,
    string SourceRevision,
    string Framework,
    string ZhinuComponent,
    string SimingComponent,
    string CreatedUtc,
    string? LiveAuthorizationRef,
    string? LiveAuthorizationHash,
    SelectedCaseEvidence[] SelectedCases);

internal sealed record BudgetUnitsEvidence(
    long ModelCalls,
    long HttpRequests,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    string? MaximumCost = null,
    string? Currency = null);

internal sealed record ReservationEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    BudgetUnitsEvidence Units);

internal sealed record BudgetDeniedEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string ReasonCode);

internal sealed record ReservationReleasedEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ReasonCode,
    BudgetUnitsEvidence Units);

internal sealed record DispatchIntentEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string ContractIds,
    string FixtureId,
    string FixtureHash,
    string BoundaryKind);

internal sealed record AttemptFinishedEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string Outcome,
    string Completeness,
    string? FailureCategory,
    string? Attribution,
    string? AttributionBasis,
    string? FailureStage,
    string? SuggestedAttributionClass,
    bool ModelInvoked,
    string[] AssertionIds,
    AssertionEvidence[] Assertions,
    StreamDiagnosticsEvidence? Diagnostics,
    RouterEvidenceRecord? Router,
    UsageEvidence? Usage,
    string? ExecutionCode);

internal sealed record UsageSettledEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    UsageEvidence Usage,
    string Settlement);

internal sealed record AttemptInterruptedEvidence(
    string SchemaVersion,
    string AttemptId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ReasonCode);

internal sealed record TrialFinishedEvidence(
    string SchemaVersion,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string Outcome,
    string[] FailedAssertionIds,
    string? FailureCategory,
    string? Attribution,
    bool Complete,
    string[] AttemptIds);

internal sealed record RunFinishedEvidence(
    string SchemaVersion,
    string RunId,
    string PlanHash,
    bool Complete,
    int Passed,
    int Failed,
    int Blocked,
    int Cancelled,
    int Inconclusive,
    int NotRun,
    long ConsumedModelCalls,
    long ConsumedHttpRequests,
    string? UnresolvedLiability);

internal sealed record ReconciliationEvidence(
    string SchemaVersion,
    string RunId,
    int InterruptedAttempts,
    int ReleasedReservations);

internal sealed record HttpDispatchAdmittedEvidence(
    string SchemaVersion,
    string AttemptId,
    string DispatchId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string Destination,
    string BoundaryKind,
    int PhysicalAttemptIndex,
    int MaximumPhysicalAttempts);

internal sealed record HttpDispatchResultEvidence(
    string SchemaVersion,
    string AttemptId,
    string DispatchId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    int? StatusCode,
    string Phase,
    long BytesRead,
    bool Completed);

internal sealed record HttpDispatchDeniedEvidence(
    string SchemaVersion,
    string AttemptId,
    string DispatchId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ReasonCode);

internal sealed record EligibilityAssessmentEvidence(
    string SchemaVersion,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string ScenarioId,
    string State,
    string[] CriticalReasons,
    string[] Limitations,
    string TransportChain,
    string? TransportProfileId,
    string? TransportProfileIdentity,
    int? PhysicalAttemptUpperBound,
    string WireAttemptBound,
    string? AllowanceRef,
    string? TokenBoundSource,
    string? CostBoundStatus);

// Workflow request: the immutable case binding Zhinu persists and replays.
internal sealed record SubjectBindingEvidence(
    string Service,
    string AccountScopeRef,
    string EndpointProfile,
    string Protocol,
    string? ApiVersion,
    string? ModelRevision,
    bool MutableAlias,
    string TransportProfile,
    string TransportProfileRef,
    string TransportProfileIdentity,
    string EndpointId,
    string Provider,
    string ProviderModel,
    string BaseUrl,
    string? Profile,
    IReadOnlyDictionary<string, string> Settings,
    string ClientRequestTimeout,
    string EffectiveAttemptCeiling,
    bool NativeToolCalling,
    string IdentityProtocol,
    string SecretSlotReference);

internal sealed record BudgetEnvelopeEvidence(
    long ModelCalls,
    long HttpRequests,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    string? MaximumCost,
    string? Currency);

internal sealed record DeadlinesEvidence(
    string AttemptTimeout,
    string MaximumDuration,
    string StreamIdleTimeout);

internal sealed record LimitsEvidence(
    long MaximumRequestBytes,
    long MaximumResponseBytes,
    long MaximumEventBytes);

internal sealed record TokenReservationEvidence(
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    string? MaximumCost = null,
    string? Currency = null);

internal sealed record CaseExecutionRequest(
    string RunId,
    string PlanHash,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string SubjectId,
    string ModelId,
    string ScenarioId,
    string[] ObservedContractIds,
    string FixtureId,
    string FixtureHash,
    string[] AssertionIds,
    bool RequiresNativeTools,
    bool RequiresRouterDiagnostics,
    int DispatchCeiling,
    int MaximumPhysicalAttempts,
    TokenReservationEvidence? PlanCaseTokenReservation,
    SubjectBindingEvidence Subject,
    BudgetEnvelopeEvidence Budget,
    DeadlinesEvidence Deadlines,
    LimitsEvidence Limits);

// Durable step results inside the workflow.
internal sealed record AttemptRecord(
    string AttemptId,
    bool BudgetDenied,
    string? DenialCode,
    bool ModelInvoked,
    AttemptOutcome Outcome,
    string Completeness,
    string? FailureCategory,
    string? Attribution,
    string? AttributionBasis,
    string? FailureStage,
    string? SuggestedAttributionClass,
    string[] FailedAssertionIds,
    AssertionEvidence[] Assertions,
    StreamDiagnosticsEvidence? Diagnostics,
    RouterEvidenceRecord? Router,
    UsageEvidence? Usage,
    string? ExecutionCode,
    bool Settled);

internal sealed record TrialOutcomeRecord(
    string AttemptId,
    AttemptOutcome Outcome,
    string[] FailedAssertionIds,
    string? FailureCategory,
    string? Attribution,
    bool Complete);

// Internal failure raised only by the evidence seam; never describes user input.
internal sealed class EvidenceIoException(string eventType, Exception inner) : Exception(
    $"Evidence append failed for event type '{eventType}'.", inner)
{
    public string EventType { get; } = eventType;
}

// Deliberate process-death simulation used by deterministic restart tests.
internal sealed class SimulatedProcessCrashException(string phase) : Exception(
    $"Simulated process crash at phase '{phase}'.")
{
    public string Phase { get; } = phase;
}

internal sealed class ExecutionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
