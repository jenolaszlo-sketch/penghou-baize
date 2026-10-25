# BQ-1b Stage 1 offline planner — evidence bundle

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Branch: `implementation/baize-bq1b-stage1`.

| Binding | Value |
| --- | --- |
| Stage 0 audited/accepted base | `c116b24835fac78d12d1eb601ad701bccef8d51e` |
| Implementation commit | `0a063fd3f578c3613e065b1432c3a64906d4260d` |
| CLI source revision embedded in `plan.json` | `0.3.0-preview.7+0a063fd3f578c3613e065b1432c3a64906d4260d` |
| Environment | Windows; .NET SDK 10.0.401; Microsoft.NETCore.App 10.0.12; Release build |

`plan.json` is a reviewed synthetic example, not live evidence. It contains no
credential values, no private prompt text and no workspace paths; its build
binding hashes are meaningful only for the exact local binaries listed above.
Nothing in this bundle authorizes network, paid or model execution.

## Commands

Executed with `MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0`
and a nonexistent `BAIZE_ENV_FILE`; the CLI was built from the implementation
commit (`dotnet build eng/Penghou.Baize.Qualification -c Release`).

```powershell
dotnet <cli> inventory validate --inventory eng/qualification/stage1/inventory.json
dotnet <cli> plan --inventory eng/qualification/stage1/inventory.json `
    --policy eng/qualification/stage1/policy.json `
    --models fake-alpha,fake-beta `
    --contracts chat.baseline@3,tools.single@3,chat.streaming-integrity@2 --out <dir>
```

The `plan` command ran twice into independent output directories: stdout
summaries and `plan.json` bytes were byte-identical across runs (deterministic
conformance observation, checked by the generating script and asserted
continuously by `Stage1CanonicalizationTests`).

## Artifacts and SHA-256 (UTF-8 without BOM, CRLF normalized to LF, including the final newline, so Git checkout line endings do not change the values)

| File | SHA-256 |
| --- | --- |
| `validation.json` | `3cc811cfd70982a3fdeeca2344080abfc946fae62765522593041dbbbcc14c30` |
| `summary.json` | `e4b30bb54702c55d8b78c095d745bb5b6b314520a66d2e727b71e72d3cc55190` |
| `plan.json` | `8c636da9d9f7e60df88edcb9082ee7bed789a29562b94c787289a539d81fba8d` |
| `identity.json` | `e4e64d13039ca38cdf6931d43fe85e245c419a005f6b9e525cb924771d4421d0` |
| `rejections.jsonl` | `28ec310234a65c05a35442887f749641008325acdd43a0d9fa16744408bc8214` |
| `eng/qualification/stage1/inventory.json` (input) | `8a445f2a18993f79e0ef25a590316d4efc9ba9beee00f587b75a6e360611df90` |
| `eng/qualification/stage1/policy.json` (input) | `34f566c5781080473b9bdc03de66e452207145a33146c880d36bc3ff71896ed2` |
| `eng/qualification/stage1/canonical-vectors.json` (input) | `34c4d56683e15292d304699e66c0cd825dea89e4d8e7135bc158cb9ee4120778` |

## What the artifacts show

- `validation.json` — `inventory validate` completes with zero secret reads,
  zero HTTP sends/client constructions and zero provider constructions. The
  inventory hash is the canonical identity of the reviewed synthetic inventory.
- `plan.json` / `summary.json` / `identity.json` — two synthetic models
  (`fake-alpha`, `fake-beta`) resolve to two separate immutable subjects; four
  planned cases (baseline and tools per model, three repetition slots each);
  `chat.streaming-integrity@2` is recorded as a shared observation on each
  invocation case without its own dispatch; planned requirements are 12
  semantic invocations / 12 neutral HTTP calls / 12 terminal backend sends with
  physical wire attempts bounded only because both profiles are
  `SyntheticFake`. Economics and token bounds are absent, so requirements carry
  nulls (never zero) and every case is `Blocked` with explicit
  `liveEligibilityBlockers` (Stage 2–5 admission not implemented, unknown
  revision/protocol evidence, unverified allowance, unknown economics). The
  plan and its stdout state `executionAuthorized=false`,
  `qualificationPerformed=false`, `liveEligibility=Blocked`.
  `identity.json` repeats the plan hash with `executionAuthorized=false`.
- `rejections.jsonl` — eight deterministic fail-closed records: unknown
  command, empty arguments, unknown model selector, historical contract
  selector, streaming without a parent scenario, unknown inventory schema
  version, credentials embedded in an endpoint URL, and an all-excluded plan.
  Each exits 2, prints only the machine-readable failure summary on stdout and
  the single reason code on stderr, and performs zero secret reads or network
  activity.
