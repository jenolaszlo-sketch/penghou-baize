# BQ-1b Stage 4B evidence — live qualification preparation

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Worktree: `Penghou.Baize.stage4b` on branch `implementation/baize-bq1b-stage4b`.
Base: `f9ffa02adec676af73a0220761e64f84fdc94826` (Stage 4A).
Implementation commit: `214a363` (live authorization interlock, candidate
fixtures, tests, Gate A stale-connection probe). The handoff commit records the
final documentation binding.

Runtime: Windows 10.0.26200, .NET SDK 10.0.401, Microsoft.NETCore.App 10.0.12.
`System.Net.Http.dll` SHA-256:
`b98ed0cb80dd54c33fc049f33af5448710752155bbd5cc8cbcd932e01f066801`.
Runner tool packages: `Penghou.Zhinu.Sqlite 0.2.0-preview.4`,
`Penghou.Siming.Sqlite 1.0.0`, `Microsoft.Extensions.Configuration.Json 10.0.9`.
Production packages are unchanged; the production source diff is empty.
`MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0`, and a
nonexistent `BAIZE_ENV_FILE` were used. No provider request, credential read or
live qualification was performed.

## Gate A evidence

Stage 4A established the physical-attempt bound of one for
`baize-sockets-http11-exact-v1`. Stage 4B adds one focused probe:

- `Stage4aLoopbackTests.StaleKeepAliveConnection_RecoversWithoutReplay` — the
  controlled server completes a first HTTP/1.1 request with keep-alive, closes
  the connection, and then serves a second independently admitted request. The
  observed counts are two guard admissions, two complete server requests and two
  connections; no third request is generated.
- The v10.0.12 `SocketsHttpHandler` source audit and its file hashes remain in
  the Stage 4A evidence bundle. The request shape is unchanged: POST with
  content, no `Expect: 100-continue`, HTTP/1.1 exact, no redirects/authentication/
  cookies/proxy/decompression/retry middleware. `HttpConnection._canRetry`
  remains false, so the connection-failure retry loop is unreachable. A TCP/TLS
  reconnection is not another HTTP request.

No Stage 4A blocker was removed to enable Stage 4B.

## External-network interlock evidence

The qualification runner now separates the commands:

- `run` is synthetic-only. `--allow-live` is parsed for compatibility but a
  non-synthetic selection is refused with
  `live-execution-requires-run-live-command` before a run directory, eligibility
  assessment, credential read or dispatch.
- `run-live` requires `--plan`, `--authorization` and `--out`. The authorization
  is a separate `qualification-live-authorization` artifact bound to the exact
  plan/contract/inventory/policy hashes, transport profile id and identity,
  provider/endpoint/model/protocol/settings, case/trial ids, budget, credential
  slot name, output directory, authorization reference, operator reference and
  validity window.
- `validate-live` performs the full offline validation and prints the proposed
  cohort and liability. It creates no run directory and does not read a
  credential value.
- `resume` of a live run requires the same authorization, re-validates it against
  the manifest and current eligibility, and does not replenish budget.
- Synthetic runs remain local; a synthetic `--allow-live` run is still labeled
  `liveAuthorized=false`.

The deterministic interlock tests cover:

| Test | Result |
| --- | --- |
| `OfflineRunWithAllowLive_RefusesRealProfileBeforeSecrets` | refused before run dir/secret |
| `OfflineRunWithAllowLiveOnSynthetic_RemainsSynthetic` | local synthetic run only |
| `RunLiveWithoutAuthorization_RefusesBeforeRunDirectory` | `live-authorization-required` |
| `RunLivePlanHashMismatch_Refused` | `live-authorization-plan-mismatch` |
| `RunLiveEndpointMismatch_Refused` | `live-authorization-endpoint-mismatch` |
| `RunLiveProfileIdentityMismatch_Refused` | `live-authorization-profile-mismatch` |
| `RunLiveBudgetOrTokenMismatch_Refused` | `live-authorization-budget-mismatch` |
| `RunLiveExpiredAuthorization_Refused` | `live-authorization-expired` |
| `RunLiveOutputMismatch_Refused` | `live-authorization-output-mismatch` |
| `RunLiveSelectionMismatch_Refused` | `live-authorization-selection-mismatch` |
| `RunLiveLoopbackAuthorization_RefusedByEligibility` | `loopback-not-live-eligible` |
| `RunLiveSyntheticAuthorization_Refused` | `live-command-requires-real-profile` |
| `ResumeLiveWithoutAuthorization_Refused` | `live-resume-requires-authorization` |
| `ResumeNonLiveWithAuthorization_Refused` | `authorization-not-applicable` |
| `LegacyManifestWithoutLiveFields_RemainsReadable` | old manifests remain readable |

All denial paths leave no run directory. The candidate validation and denial
tests use the real Release runner but no external endpoint.

## Candidate plan and offline validation

```powershell
$cli = "eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll"
dotnet $cli plan --inventory eng\qualification\stage4b\inventory.json --policy eng\qualification\stage4b\policy.json --models openrouter-llama31-8b-smoke --contracts chat.baseline@3,chat.streaming-integrity@2 --out artifacts\qualification\stage4b-preparation\plan-candidate
dotnet $cli validate-live --plan artifacts\qualification\stage4b-preparation\plan-candidate\plan.json --authorization artifacts\qualification\stage4b-preparation\test-authorization.json --out artifacts\qualification\stage4b-openrouter-llama31-8b-smoke
```

`validate-live` exited 0 with `kind=live-authorization-validated`,
`executionAuthorized=false`, `qualificationPerformed=false`, and created no run
directory. The authorization used in this offline validation is a test-only
artifact in the ignored `artifacts/` directory; it is not the proposed operator
authorization.

| Binding | Value |
| --- | --- |
| Plan hash | `sha256:d0b52fec3c97e26f80a93579c1717c3bbdec97c71bad2ebdbc567da27741f44f` |
| Source revision | `0.3.0-preview.7+214a363a7e3ffe60325016ebd986427979687840` |
| Inventory hash | `sha256:6018f06c531e50560d176d2026297f783b7c61515c5b33ba2844c30cecf8b66a` |
| Policy hash | `sha256:84f052813bca45acd01a6fe1af8456ca3ccc50348813d6e834ad8d8859fc44e9` |
| Contract registry hash | `sha256:ce2503aca92774ea74878290ee2a9b95d985ba24e8eae66876db5654f1d90db8` |
| Transport profile | `baize-sockets-http11-exact-v1` |
| Transport identity | `sha256:88486795d1c6630425c34e0e3cf4c60fafda5af185928b8e759257f904930adb` |
| Case id | `sha256:63a2827688ad9a3d535cc0803eaf77ec3fedf1e6bc5a635cd17fcb3613afd294` |
| Trial id | `sha256:0c0a60bf217368ecb37e130175b2cd5715b5aec3e0b598cc04b57649ce96a819` |
| Test authorization hash | `sha256:1f50cb40fae38a5b5321ea5181f91cff1ab97404b625925c4363cfd802680dc6` |
| Maximum model calls | 1 |
| Maximum HTTP requests | 1 |
| Token maximum | 256 input / 512 output / 0 reasoning / 768 total |
| Maximum liability | `0.0004 USD` |
| Proposed artifact destination | `artifacts/qualification/stage4b-openrouter-llama31-8b-smoke` |

## Proposed manifest

The committed
[`model-qualification-bq1b-stage4b-proposed-live-manifest.json`](../../model-qualification-bq1b-stage4b-proposed-live-manifest.json)
is a `qualification-live-run-proposal`, not an authorization. Its SHA-256 is
`f6d4fbc1049c34e823d4e246b63c4ef22bd6b05419ee4f47f9f6b9a2b79a3c1d` at the
implementation commit. It contains the exact plan hashes, case/trial ids,
transport identity, endpoint/model/settings, budget, maximum liability, retained
restrictions and an authorization template with pending operator fields.

## Artifact hashes (SHA-256, lowercase hex)

| File (relative to `artifacts/qualification/stage4b-preparation/`) | Hash |
| --- | --- |
| `plan-candidate/plan.json` | `d0b52fec3c97e26f80a93579c1717c3bbdec97c71bad2ebdbc567da27741f44f` |
| `plan-candidate/identity.json` | `6cf3e5c0c244d4858b9bb129c5737e78cf9e581d534739681bc4f90ad326a98f` |
| `test-authorization.json` | `3d6fb1e097c0948caad2ad82a933643e0fde86949e17b01a150cdf25e3fca85c` |
| `validate-live-output.json` | `03460d9cdc16b30867c962261dd71956cbe3124c57e4edc53519e9b93c4246c1` |

The test authorization and validation output are ignored artifacts, not
operator authorization.

## Deterministic suite

- Qualification project: **309 passed, 0 failed**.
- Full solution: **2,122 passed, 28 existing skips, 0 failures** across 26
  project/framework executions.
- Release build: 0 warnings / 0 errors.
- `dotnet format --verify-no-changes`: only the three pre-existing
  `JsonSchemaGenerator.cs` findings.
- `git diff --check`: clean.
- `git diff -- src/`: empty.
- Live provider requests / live credentials / publication: zero.
- The Stage 4A process deviation remains disclosed in the Stage 4A audit/handoff;
  no live profile execution is present in the final Stage 4B tests or evidence.
