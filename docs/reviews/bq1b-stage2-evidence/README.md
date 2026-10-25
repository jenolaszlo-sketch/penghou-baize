# BQ-1b Stage 2 evidence — durable qualification execution slice

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Worktree: `Penghou.Baize.stage2` on branch `implementation/baize-bq1b-stage2`.

This bundle records the deterministic CLI executions, the exact hashes and the
fault-injection suite that demonstrate the Stage 2 completion criterion:

**Frozen plan → Zhinu workflow → Baize synthetic invocation → BQ-1a
assertions → Siming evidence → qualification report.**

Runtime: Windows, .NET SDK 10.0.401, Microsoft.NETCore.App 10.0.12; commands
ran with `MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0` and a
nonexistent `BAIZE_ENV_FILE`; no live provider requests, no secret reads, no
publication occurred. Outputs live under the ignored
`artifacts/qualification/stage2-evidence/` directory; the hashes below bind
those artifacts.

## Component pinning (recorded in every run manifest)

| Component | Package version | Role |
| --- | --- | --- |
| Penghou.Zhinu / Penghou.Zhinu.Sqlite | `0.2.0-preview.4` | Durable workflow lifecycle: step commits, leases, restart, cancellation |
| Penghou.Siming / Penghou.Siming.Sqlite | `1.0.0` | Append-only hash-chained evidence ledger, idempotent appends, checkpoints |

Both are tool-only dependencies of the `eng/` CLI; the production dependency
closures remain the previously verified 12 packages (`verify-transport-dependencies.ps1`
re-verified after the change).

## CLI evidence — mixed pass/fail run (exit 3 preserved through resume)

```powershell
$cli = "eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll"
dotnet $cli plan --inventory eng\qualification\stage2\inventory.json --policy eng\qualification\stage2\policy.json --models fake-alpha,fake-wrong --contracts chat.baseline@3 --out artifacts\qualification\stage2-evidence\plan
dotnet $cli run --plan artifacts\qualification\stage2-evidence\plan\plan.json --out artifacts\qualification\stage2-evidence\run --models fake-alpha,fake-wrong --contracts chat.baseline@3 --trial 1
dotnet $cli resume --run artifacts\qualification\stage2-evidence\run
dotnet $cli report --run artifacts\qualification\stage2-evidence\run
```

Observed: `plan` exit 0 (plan-generated, `executionAuthorized=false`,
`liveEligibility=Blocked`); `run` exit 3 with
`outcome=contract-failure`, `ledgerVerified=true`, `checkpointCaptured=true`;
`resume` exit 3 with a **byte-identical** JSON summary
(`sha256:121f3d43ca76cb4eb8c15c447cc03d94a66ae4dd14ccd2a689be8dbb1d9d1234`),
proving idempotent re-execution; `report` exit 3 with `checkpointVerified=true`
and `complete=true`. The report shows `fake-alpha` Passed and `fake-wrong`
Failed (`response.expected-value`), never conflating them.

## CLI evidence — all-pass run (baseline + tools)

```powershell
dotnet $cli plan --inventory eng\qualification\stage2\inventory.json --policy eng\qualification\stage2\policy.json --models fake-alpha --contracts chat.baseline@3,tools.single@3 --out artifacts\qualification\stage2-evidence\plan-allpass
dotnet $cli run --plan artifacts\qualification\stage2-evidence\plan-allpass\plan.json --out artifacts\qualification\stage2-evidence\run-allpass --models fake-alpha --contracts chat.baseline@3,tools.single@3 --trial 1
```

Exit 0, `outcome=all-selected-cases-passed`; two distinct case/trial/attempt
identities, one synthetic terminal send each, observed usage settled from the
adapter-parsed usage chunk (`promptTokens=12, completionTokens=3` for
baseline).

## Artifact hashes (SHA-256, lowercase hex)

| File (relative to `artifacts/qualification/stage2-evidence/`) | Hash |
| --- | --- |
| `plan/plan.json` | `4e1d05c5395e909fee4c0b66ed01441d28bf5837dfd569c82ec023cd6359654c` |
| `plan/identity.json` | `1a38a1ae588fde6c5bcde21d59f7ba9840a5ce9b93427c807977225f483f747d` |
| `run/plan.json` (byte-identical copy) | `4e1d05c5395e909fee4c0b66ed01441d28bf5837dfd569c82ec023cd6359654c` |
| `run/manifest.json` | `c3737827ecab4bce4459caa39c493e3740e5478d19cd9a83b33be4421e66ab99` |
| `run/checkpoint.json` | `092edff51bac919d27d10ce8b39d679ed5e0e72b3e1869cd9b5e7055d6938d9d` |
| `run/reports/summary.json` | `fe1a447c48a3ea53a20cb2e734deee0f41960dbb8a047f4f501486dd6903c4e1` |
| `run/reports/report.md` | `df02d2ab1888e54999b13ba6b63dc6ad03b0ab4e5cb9ad543ea859d8eb75fc32` |
| `run-summary.json` / `resume-summary.json` | `121f3d43ca76cb4eb8c15c447cc03d94a66ae4dd14ccd2a689be8dbb1d9d1234` |
| `report-summary.json` | `c225adf94b9266d1d49c972a61dbeb09b975f768526d032b3ece6e4807ac2c2f` |
| `plan-allpass/plan.json` | `6bb6c7623c3deee5cb2eba88db15fe230a5cb209b5b2239c86399e1dc9280aa3` |
| `run-allpass/checkpoint.json` | `a3f614fc462de0d7880ac90244ba51dddeb9861c15cff1cd44fbbdd4cd46b1c9` |

SQLite stores (`evidence.db`, `workflow.db`) are content-addressed by the
Siming checkpoint and the Zhinu run records; their file hashes above are
informational only (SQLite files are not byte-deterministic).

## Deterministic fault/restart suite

`tests/Penghou.Baize.Qualification.Tests` gained 25 deterministic Stage 2
tests (`Stage2ExecutionTests`, `Stage2RecoveryTests`,
`Stage2CancellationAndEvidenceTests`, `Stage2AdmissionTests`), all using the
real pinned Zhinu and Siming stores over local files — never mocks of the two
components. Coverage mapping to the acceptance list:

| Requirement | Test(s) |
| --- | --- |
| 1 Successful workflow and evidence recording | `BaselineSyntheticCase_CompletesWithVerifiedEvidenceAndReport`, `ToolSyntheticCase_PassesSingleToolContract` |
| 2 Contract failure with preserved negatives | `WrongValueBaseline_FailsWithPreservedNegativeAssertions` |
| 3 Cancellation and incomplete observation | `Cancellation_PreservesIncompleteObservation` (exit 130, no trial finalization, interrupted attempt reconciled as spent-unknown) |
| 4 Process restart after a completed step | `CrashAfterWorkflowsCompleted_ResumeCompletesWithoutReinvocation` |
| 5 Failure between workflow completion and Siming append | `TrialFinishedAppendFailure_ResumeReplaysIdempotently` (Zhinu step restart re-runs the failed finalize; the idempotent append completes without a second invocation) |
| 6 Repeated Siming append, same event identity | `RepeatedResume_NeverDuplicatesEvidence` (three resume passes keep every event unique) |
| 7 Legitimate re-execution as a distinct attempt | `InterruptionAfterIntent_ResumeCreatesDistinctAttempt` (2 reservations, 2 intents, 1 interrupted + 1 passed attempt; budget retains both slots) |
| 8 Evidence storage failure cannot produce a green report | `EvidenceStorageFailure_ProducesNoSuccessfulReport` (exit 5, no report files) |
| 9 Budget exhaustion and unknown usage | `BudgetExhaustion_PreventsFurtherExecutionAndSurvivesResume`, `MissingUsage_IsSettledAsSpentUnknown`, `AttemptTimeout_RecordsBoundedBlockedObservation` |
| 10 Multiple cases without identity/budget contamination | `MultipleCases_KeepSeparateIdentitiesAndMixedOutcomeIsVisible` |
| Shared allowance concurrency | `ConcurrentReservations_SingleFinalSlotWins` (8 racing reservations admit exactly 1) |
| Tamper/identity gates | `TamperedEvidenceLedger_FailsVerificationAndReport`, `TamperedRunPlan_RefusesResume`, `ModifiedPlanBytes_RefuseExecutionBeforeSecrets`, `UnknownSelectionAndTrial_RefuseExecution`, `ReviewedNetworkProfile_RefusesExecution`, `ExcludedCase_RefusesExecution`, `UnknownObservedContract_FailsBinding` |
| Privacy | `SentinelAndPromptText_NeverPersist` (no prompt content, sentinel secret or fixture text anywhere in the run directory) |
| Eligibility vocabulary | `SyntheticOpenAi_PlansWithoutRetiredStageGapBlockers`, `OllamaSynthetic_PlansWithExplicitTransportBlocker` |
| Provider failure attribution | `UnavailableProvider_IsBlockedNotFailed` (Blocked/provider-unavailable, never a model capability failure) |

All 25 Stage 2 tests pass deterministically; the full solution test suite
reports **2,053 passed, 28 existing skips, 0 failures** across 25
project/framework executions. The one transient observation from Stage 1
(`StreamAsync_EmitsPrivacySafeIntegrityActivity`) did not recur; one other
pre-existing load-sensitive test (`StreamAsync_RejectsIncompleteToolCallAfterCleanProviderEnd`)
failed once in a full parallel run and passed in isolation and in the complete
rerun — same flake category, no source related to it was modified.

## Gate results

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Production source diff | Empty (`git diff -- src/`); all changes are `eng/`, `tests/`, `docs/` |
| Full deterministic solution tests | 2,053 passed, 28 existing skips, 0 failures |
| Production dependency closures | 12/12 verified, exact public transport packages, no Hufu |
| `dotnet format --verify-no-changes` | Only the three pre-existing `JsonSchemaGenerator.cs` findings (lines 19/44/52, blob unchanged since `8e559db`); all new files format-clean |
| `git diff --check` | Clean |
| Live requests / secret reads / publication | Zero |
