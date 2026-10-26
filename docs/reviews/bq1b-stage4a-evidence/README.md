# BQ-1b Stage 4A evidence — physical transport qualification

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Worktree: `Penghou.Baize.stage4a` on branch `implementation/baize-bq1b-stage4a`.
Base: `d10ea1d79a7c390b764ccf97321732f4c087e80d` (Stage 3).
Implementation commits: `db6ee2c` (profile, probes, runner integration) and
`40a5702` (unreviewed eligibility vocabulary). The handoff commit records the
final documentation binding.

Runtime: Windows 10.0.26200, .NET SDK 10.0.401, Microsoft.NETCore.App 10.0.12.
`System.Net.Http.dll` SHA-256:
`b98ed0cb80dd54c33fc049f33af5448710752155bbd5cc8cbcd932e01f066801`.
The plan build source revision is
`0.3.0-preview.7+40a570212e184e2dca51e3dfa1b4af23ad08c3b3`.
`MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0`, and a
nonexistent `BAIZE_ENV_FILE` were used. Outputs live under the ignored
`artifacts/qualification/stage4a-evidence/` directory. No provider request,
credential read or publication was intentionally made in the final evidence
commands.

## CLI evidence

```powershell
$cli = "eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll"
dotnet $cli plan --inventory eng\qualification\stage4a\inventory.json --policy eng\qualification\stage4a\policy.json --models fake-alpha --contracts chat.baseline@3,tools.single@3 --out artifacts\qualification\stage4a-evidence\plan-synthetic
dotnet $cli run --plan artifacts\qualification\stage4a-evidence\plan-synthetic\plan.json --out artifacts\qualification\stage4a-evidence\run-synthetic --models fake-alpha --contracts chat.baseline@3,tools.single@3 --trial 1
dotnet $cli plan --inventory eng\qualification\stage4a\inventory.json --policy eng\qualification\stage4a\policy.json --models openrouter-llama31-8b --contracts chat.baseline@3 --out artifacts\qualification\stage4a-evidence\plan-openrouter
dotnet $cli plan --inventory eng\qualification\stage4a\inventory.json --policy eng\qualification\stage4a\policy.json --models loopback-local-alpha --contracts chat.baseline@3 --out artifacts\qualification\stage4a-evidence\plan-loopback
dotnet $cli run --allow-live --plan artifacts\qualification\stage4a-evidence\plan-loopback\plan.json --out artifacts\qualification\stage4a-evidence\run-loopback-refused --models loopback-local-alpha --contracts chat.baseline@3 --trial 1
dotnet $cli plan --inventory eng\qualification\stage4a\inventory.json --policy eng\qualification\stage4a\policy.json --models deepseek-native-chat --contracts chat.baseline@3 --out artifacts\qualification\stage4a-evidence\plan-unreviewed
dotnet $cli run --allow-live --plan artifacts\qualification\stage4a-evidence\plan-unreviewed\plan.json --out artifacts\qualification\stage4a-evidence\run-unreviewed-refused --models deepseek-native-chat --contracts chat.baseline@3 --trial 1
```

The synthetic run exits 0 with verified evidence and a report. The OpenRouter
plan is generated but **not executed**. The loopback `--allow-live` command is
refused with exit 2 and `criticalReasons=["loopback-not-live-eligible"]`. The
unreviewed DeepSeek profile is refused with exit 2 and
`criticalReasons=["allowance-unverified","token-reservation-bound-unknown","transport-profile-unreviewed","wire-attempt-bound-unproven"]`.

The OpenRouter plan records:

```text
profileId = baize-sockets-http11-exact-v1
state = Qualified
physicalAttemptUpperBound = 1
identity = sha256:88486795d1c6630425c34e0e3cf4c60fafda5af185928b8e759257f904930adb
requirements.physicalWireAttempts = 3
cases[0].maximumPhysicalWireAttemptsPerSlot = 1
```

## Artifact hashes (SHA-256, lowercase hex)

| File (relative to `artifacts/qualification/stage4a-evidence/`) | Hash |
| --- | --- |
| `plan-synthetic/plan.json` | `1e05313c00d2fd05df7bb6c4bd7078b0cfd47f60c9128048457eb717e58fc577` |
| `run-synthetic/manifest.json` | `351f6be6db1e424838ca5d01f10262f01be01fc2b6f0a325c560f532f7639dbb` |
| `run-synthetic/checkpoint.json` | `92f4a763d5bc5ade1fe5a7c5892f24f5113470cc4ba7dead842b156650df7a77` |
| `run-synthetic/reports/summary.json` | `19da98e7469b65762a66475832175559ab9cb110ef550c35bfdcd3e0b25b6659` |
| `run-synthetic/reports/report.md` | `3c6f79c3df20c7e815e930974a796ee77810796cc891ac79f3345edc0dfb624a` |
| `plan-openrouter/plan.json` | `c2760a6ec90a3cc9acbbf93c5ac06d76d9db421f061608d28d0d06ae5b661767` |
| `plan-openrouter/identity.json` | `b6671a10c869bf5b7adb6ca6ae58ee3025ef9d717ad63e7124f0b44d57158855` |
| `plan-loopback/plan.json` | `8e5f0e331445284c5a60d61015e7252cba56093f110f248ac38b5b86f3f5d654` |
| `run-loopback-refusal.json` | `5d11413f07456ade84b3e869d69fb4fc64cb5450b05c58805baa8f685c9ff473` |
| `plan-unreviewed/plan.json` | `f490dff7b2bb90673bdcb0eef5a777f3dc45c0506b492294539ce05af909504a` |
| `run-unreviewed-refusal.json` | `14f16877984619c24caa7adcb315fdfed4e61294e881d3812f86ddf5478f7671` |

SQLite files (`evidence.db`, `workflow.db`) are not byte-deterministic; integrity
is carried by the Siming hash chain, checkpoint and verification result.

## Runtime source inspection hashes

The audit inspected tagged `dotnet/runtime` v10.0.12 source files. These are
supporting source artifacts, not repository files.

| File | SHA-256 |
| --- | --- |
| `HttpClient.cs` | `4863135632cecd06c9a2afa2010d4955a1586c4f677fbfbff7bc6b4b9a6d4d43` |
| `HttpRequestMessage.cs` | `0cac1dad528a7f7ab1efa896fa86712d8ad33734d1c213f091965cf00abc99c4` |
| `SocketsHttpHandler/HttpConnection.cs` | `858c0dd2fcab42b82e0ba754edb9a0a817a9310d0afffa7a796373e7f8edc8dc` |
| `SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs` | `f3667bb44c53edd9358de811763b044676e152b3b922a9e9c43bfdffd7c5e425` |
| `SocketsHttpHandler/HttpConnectionHandler.cs` | `c39722e26d4c72251b5427465b7c5e76280b167f8c53b74999c412989bba2104` |
| `SocketsHttpHandler/SocketsHttpHandler.cs` | `87e32e3abaa2740cb16e9afedd37fd4a3d2b0586428b861628152f5fd0e9833c` |
| `SocketsHttpHandler/HttpConnectionSettings.cs` | `d70eccb455a49155b359da525db805e620df2aad1361147cc9390b7d980c5c61` |
| `SocketsHttpHandler/AuthenticationHelper.cs` | `ebfe155a1e9876118183bd20e3147dd9774c967ed2338ef27cf0c0294170dad5` |
| `SocketsHttpHandler/Http2Connection.cs` | `b638f4c3fb8f7b79fce9d18bea4e8f1fa2c6930d9949fdb3f7fd1f94cf52ccd6` |
| `SocketsHttpHandler/Http2Stream.cs` | `250a6f2d34fd4acca0700ee1dcd77128fb3a77860d65b37596953a7a5c612401` |

## Deterministic suite

28 new Stage 4A tests plus the unchanged Stage 1–3/BQ-1a tests pass:
**2,105 passed, 28 existing skips, 0 failures** across 26 project/framework
executions. The qualification project alone has **292 passed, 0 failed**.

Key evidence:

- The reviewed profile identity is reproducible and includes the runtime
  `System.Net.Http` assembly hash.
- The actual Baize OpenAI adapter, neutral transport, admission handler and
  `SocketsHttpHandler` run against a local raw-TCP server; no provider endpoint
  is used.
- Fresh and pooled connections, streaming, connection refusal, close before
  headers/body, close after body, timeout, partial response, idle deadline, read
  cancellation, redirect, 401 challenge, retry middleware, and pool recovery are
  each observed with explicit request/connection counts.
- A hidden retry above the guard is admitted once and denied with
  `physical-attempt-bound-exceeded`; the server receives one request.
- A faulting response-body disposal releases the concurrency slot; a second
  admission succeeds.
- Budget units scale with the physical-attempt bound; the reviewed profile
  reserves one HTTP request and the full token/cost envelope.
- A substituted profile identity blocks eligibility with
  `transport-profile-substituted` and `wire-attempt-bound-unproven`.
- Reopening the Siming ledger retains reserved liability; resume cannot
  replenish it.
- Synthetic reports carry `transportProfileId`, identity, the null synthetic
  bound and `wireAttemptBound = not-applicable-synthetic`.

## Gates

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Production source diff | Empty (`git diff -- src/`) |
| Full deterministic solution tests | 2,105 passed, 28 existing skips, 0 failures |
| Qualification tests | 292 passed, 0 failed |
| `dotnet format --verify-no-changes` | Only the three pre-existing `JsonSchemaGenerator.cs` findings |
| `git diff --check` | Clean |
| Live provider requests in final evidence commands | Zero |
| Live credentials / publication | Zero |

## Process deviation

An intermediate Stage 3 privacy test still called `run --allow-live` for the
newly eligible OpenRouter profile before it was corrected. It used a sentinel
environment value, not a real credential, and exited through the failure path
without producing qualification evidence. The test now exercises a
still-ineligible profile. Because the request may have left the machine, Stage
4A does not claim zero outbound requests during development; see the transport
audit's "Process deviation" section. No valid credential or paid qualification
run was intentionally made.
