# BQ-1b Stage 3 evidence — real transport readiness

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Worktree: `Penghou.Baize.stage3` on branch `implementation/baize-bq1b-stage3`.

This bundle records the deterministic CLI executions, hashes and the fault
injection suite demonstrating Stage 3: the real Baize adapter composition
over a guarded transport, fail-closed live eligibility, and compatibility
reporting. Runtime: Windows, .NET SDK 10.0.401, Microsoft.NETCore.App
10.0.12; `MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0`,
nonexistent `BAIZE_ENV_FILE`. No live provider call, secret read or
publication occurred. Outputs live under the ignored
`artifacts/qualification/stage3-evidence/` directory.

## CLI evidence — guarded synthetic composition (exit 0)

```powershell
$cli = "eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll"
dotnet $cli plan --inventory eng\qualification\stage3\inventory.json --policy eng\qualification\stage3\policy.json --models fake-alpha --contracts chat.baseline@3,tools.single@3 --out artifacts\qualification\stage3-evidence\plan-allpass
dotnet $cli run --plan artifacts\qualification\stage3-evidence\plan-allpass\plan.json --out artifacts\qualification\stage3-evidence\run-allpass --models fake-alpha --contracts chat.baseline@3,tools.single@3 --trial 1
```

Exit 0; per attempt the evidence chain is
`AttemptReserved → DispatchIntent → HttpDispatchAdmitted →
HttpDispatchResult(headers-received) → HttpDispatchResult(complete) →
AttemptFinished → UsageSettled`, with the eligibility assessment recorded
(`SyntheticOnly`).

## CLI evidence — preserved F1 adapter defect (exit 3)

```powershell
dotnet $cli plan --inventory eng\qualification\stage3\inventory.json --policy eng\qualification\stage3\policy.json --models fake-coterminal --contracts tools.single@3,chat.streaming-integrity@2 --out artifacts\qualification\stage3-evidence\plan-f1
dotnet $cli run --plan artifacts\qualification\stage3-evidence\plan-f1\plan.json --out artifacts\qualification\stage3-evidence\run-f1 --models fake-coterminal --contracts tools.single@3,chat.streaming-integrity@2 --trial 1
```

Exit 3; the strict `stream.sequence` assertion fails with
`invalid-event-sequence` and the report records
`stage: assertion; suggested attribution: baize-adapter-defect; recorded
attribution: contract-assertion-failure` — the defect stays reproducible and
never waives the contract.

## CLI evidence — fail-closed live eligibility (exit 2)

```powershell
dotnet $cli plan --inventory eng\qualification\stage3\inventory.json --policy eng\qualification\stage3\policy.json --models openrouter-llama31-8b --contracts chat.baseline@3 --out artifacts\qualification\stage3-evidence\plan-openrouter
dotnet $cli run --allow-live --plan artifacts\qualification\stage3-evidence\plan-openrouter\plan.json --out artifacts\qualification\stage3-evidence\run-openrouter-refused --models openrouter-llama31-8b --contracts chat.baseline@3 --trial 1
```

Exit 2, `kind=run-refused`, `code=live-execution-not-eligible`,
`criticalReasons=["wire-attempt-bound-unproven"]`,
`limitations=["protocol-revision-evidence-unverified"]` — a fully bound paid
profile is refused on exactly the honest wire-attempt gap. Without
`--allow-live` the same profile refuses as `real-network-not-authorized`
before any secret or dispatch.

## Artifact hashes (SHA-256, lowercase hex)

| File (relative to `artifacts/qualification/stage3-evidence/`) | Hash |
| --- | --- |
| `plan-allpass/plan.json` | `9396d8cde3d45caa2ac4781ca3862aca08c14c63281cb10ea1e96118786473c5` |
| `run-allpass/manifest.json` | `957a9833727ef82934396da639b3d8bcda2bd8cf85d272d209738f4bcf4a9c18` |
| `run-allpass/checkpoint.json` | `f826bb13851cc35ac0668e3af98c7abc4fd8e5faadf4a08e30e6e662dd05fc57` |
| `run-allpass/reports/summary.json` | `74fa411035ef890cfbcda32600212c19f1141de99f375f530270f7b40702c312` |
| `plan-f1/plan.json` | `c12f30b624ec52bccdb45d6a3b79bee9d183048655e7890a0059f6bbad4210d4` |
| `run-f1/checkpoint.json` | `de1f795b5fdbe8dcd781bb695492c5ee351767f106bc3a2ec20c323c6ffca348` |
| `run-f1/reports/summary.json` | `3da1037b7f5a282bac7e02fa8ab7540b469362d39ce2233528f69d1da8f1eee2` |
| `run-f1/reports/report.md` | `a3d8baf0f5f85cc7804abe4d9cc1db280706c263a3d17f457c934468f4efe43c` |
| `plan-openrouter/plan.json` | `380ce1fafd24588cb8a122e6ef7db5bbfece45fbfabfe23406ecc76275dc2fe0` |
| `run-openrouter-refusal.json` | `743dfde97500aeaaafba5e1cc5cc16b4f3927e8274cc568b639b9cb6c6c2d714` |

## Deterministic suite

24 new Stage 3 tests plus the unchanged 240 Stage 1/2/BQ-1a tests pass;
full solution: **2,077 passed, 28 existing skips, 0 failures** across 25
project/framework executions. Key fault-injection evidence:

- 8 racing budget reservations admit exactly one (Siming CAS); a busy
  SQLite retry path was added after a load-sensitive flake exposed it.
- 4 concurrent sends through the guard observe maximum concurrency one; the
  released-slot regression it caught is fixed and pinned by the test.
- A retry wrapper above the guard is counted; the second dispatch is denied
  at the cap with denial evidence.
- Stalled stream reads fail at the idle deadline; oversized bodies stop at
  the configured byte ceiling.
- The loopback integration runs the real SocketsHttpHandler against an
  in-process listener: adapter, guard, lease-aware secrets and the real
  stack, zero provider contact.
- Byte-scans of every run-directory file find no prompt content, tool
  argument, sentinel secret or placeholder credential.

## Gates

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Production source diff | Empty (`git diff -- src/`) |
| Full deterministic solution tests | 2,077 passed, 28 existing skips, 0 failures |
| Production dependency closures | 12/12 verified, no Hufu |
| `dotnet format --verify-no-changes` | Only the three pre-existing `JsonSchemaGenerator.cs` findings |
| `git diff --check` | Clean |
| Live requests / secret reads / publication | Zero |
