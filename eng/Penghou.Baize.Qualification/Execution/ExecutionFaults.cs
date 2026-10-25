namespace Penghou.Baize.Qualification.Execution;

// Deterministic fault injection seams for the acceptance suite. Production
// composition always uses None; tests inject failures at named boundaries
// while both Zhinu and Siming remain real components on real files.
internal sealed class ExecutionFaults
{
    public static ExecutionFaults None { get; } = new();

    // Returns true when the named evidence event append must fail.
    public Func<string, bool>? FailEvidenceAppend { get; init; }

    public Action<string>? BeforeReservationAppend { get; init; }
    public Action<string>? AfterReservationAppend { get; init; }
    public Action<string>? BeforeIntentAppend { get; init; }
    public Action<string>? AfterIntentAppend { get; init; }
    public Action<string>? BeforeInvocation { get; init; }
    public Action<string>? AfterInvocation { get; init; }
    public Action<string>? BeforeFinishedAppend { get; init; }
    public Action<string>? AfterFinishedAppend { get; init; }
    public Action<string>? BeforeSettlementAppend { get; init; }
    public Action<string>? AfterSettlementAppend { get; init; }
    public Action<string>? BeforeTrialFinishedAppend { get; init; }
    public Action<string>? AfterTrialFinishedAppend { get; init; }

    // HTTP admission boundary hooks (dispatch id / attempt id).
    public Action<string>? BeforeHttpAdmission { get; init; }
    public Func<string, bool>? DenyHttpAdmission { get; init; }
    public Action<string>? AfterHttpAdmission { get; init; }
    public Action<string>? BeforeHttpResultRecord { get; init; }
    public Action<string>? AfterHttpResultRecord { get; init; }

    // Simulates process death inside a running workflow: cancelling this token
    // stops the current Zhinu worker attempt (lease released, run remains
    // Running, nothing committed). The runner converts it into a crash.
    public CancellationTokenSource? Interrupt { get; init; }

    // Runner-level crash points, outside any workflow boundary.
    public Action? AfterRunStartedAppend { get; init; }
    public Action? AfterWorkflowsCompleted { get; init; }

    public void Crash(string phase) => throw new SimulatedProcessCrashException(phase);
}
