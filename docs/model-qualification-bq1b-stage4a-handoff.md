# BQ-1b Stage 4A handoff — physical transport qualification

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Disposition: QUALIFIED WITH RESTRICTIONS.** One exact transport profile,
`baize-sockets-http11-exact-v1`, has a reviewed upper bound of **one physical
HTTP request attempt per admitted inference invocation**. The runner enforces
that bound at the admission gate and reserves the corresponding worst-case
budget before credentials or dispatch. The profile is deliberately narrow:
HTTP/1.1 exact, no redirects, no configured authentication, no cookies, no proxy,
no decompression, no retry middleware, one connection per server. HTTP/2/3,
proxy, redirect, authentication-challenge and custom-handler paths remain
unsupported for live eligibility. Provider billing semantics remain a Stage 4B
dependency. No live model qualification was performed.

| Binding | Value |
| --- | --- |
| Base (Stage 3 accepted) | `d10ea1d79a7c390b764ccf97321732f4c087e80d` |
| Implementation commits | `db6ee2c` (profile, probes, runner integration), `40a5702` (unreviewed eligibility vocabulary) |
| Dedicated worktree | `Penghou.Baize.stage4a` on branch `implementation/baize-bq1b-stage4a` |
| Final documentation commit | the commit containing this handoff (see `git log -1`) |
| Transport audit | [model-qualification-bq1b-stage4a-transport-audit.md](model-qualification-bq1b-stage4a-transport-audit.md) |
| Evidence bundle | [bq1b-stage4a-evidence](reviews/bq1b-stage4a-evidence/README.md) |
| Plan fixture | `eng/qualification/stage4a/` |
| Runtime | .NET SDK 10.0.401; Microsoft.NETCore.App 10.0.12; `System.Net.Http.dll` `b98ed0cb...f066801` |
| Tool packages | `Penghou.Zhinu.Sqlite 0.2.0-preview.4`, `Penghou.Siming.Sqlite 1.0.0`, `Microsoft.Extensions.Configuration.Json 10.0.9` |
| Production packages | unchanged; `git diff -- src/` empty and the existing 12-package production closure is not modified |

`git diff -- src/` is empty: zero production source changes. The Stage 1–3
fixtures, accepted contracts, F1 adapter-defect evidence and historical review
bundles are unchanged. Plans are regenerated with the current runner build by
design; the Stage 4A fixture is separate from the historical Stage 3 fixture.

## What Stage 4A delivered

**Reviewed transport profile.** A runner-owned profile records the exact
adapter path, handler settings, HTTP/1.1 exact policy, physical-attempt bound,
runtime identity and `System.Net.Http` assembly hash. The canonical profile
identity is stored in the frozen plan subject; any material change to the
descriptor, bound or runtime invalidates eligibility.

**Guard hardening.** `QualificationAdmissionHandler` now:

- pins every admitted request to `HttpVersion.Version11` with
  `HttpVersionPolicy.RequestVersionExact`;
- rejects contentless requests and `Expect: 100-continue`, closing the
  `SocketsHttpHandler` connection-failure replay path for the reviewed request
  shape;
- enforces the per-attempt physical-attempt bound in addition to the global
  request cap;
- releases the concurrency slot even when response-body disposal faults.

**Budget integration.** Reservation units are scaled by the accepted physical
bound: the reviewed profile reserves one model call, one HTTP request and the
full per-attempt token/cost envelope before credentials. The guard denies a
second dispatch for the same attempt with `physical-attempt-bound-exceeded` even
when the global policy still has request allowance.

**Eligibility and evidence.** `wire-attempt-bound-unproven` is removed only for
subjects whose complete recorded transport descriptor re-hashes to the compiled
profile and runtime identity. Unreviewed profiles retain it. Loopback profiles
use the same handler but remain blocked with `loopback-not-live-eligible`.
Reports carry profile identity, physical-attempt bound, transport chain and
wire-attempt claim separately from application-level observations.

**Deterministic probes.** The actual Baize OpenAI adapter, neutral transport,
guard and `SocketsHttpHandler` run against a local raw-TCP server. The matrix
covers fresh/reused connections, streaming, connection refusal, close before
request/headers/body, timeout, partial response, idle deadline, read
cancellation, redirects, 401 challenges, unexpected retry middleware, pool
recovery and faulting body disposal. No provider endpoint is contacted.

## Physical-attempt claim

For the exact profile and request shape, the applicable v10.0.12
`SocketsHttpHandler` path has no request-replay branch:

- `HttpConnection._canRetry` is false for a POST with content and no
  `Expect: 100-continue`; the only code that sets it true requires a contentless
  request or `Expect` after request headers were sent.
- The connection-pool `MaxConnectionFailureRetries` loop is therefore
  unreachable for this request.
- HTTP/2 stream-limit replay and version fallback are unreachable under
  HTTP/1.1 exact.
- Redirects, configured authentication, cookies and proxy are disabled.
- The runner has no retry middleware below the guard and the selected router
  uses one endpoint with `MaximumAttempts = 1`.

The claim is **at most one physical HTTP request attempt**, not exactly-once
receipt or exactly-once billing. TCP connection attempts, DNS, TLS handshakes
and OS socket retries are outside the HTTP-attempt count. Provider charging
semantics, complete token billing and account allowance remain external facts.

## Acceptance coverage

| Requirement | Evidence |
| --- | --- |
| 1. Exact reproducible profile identity | `Stage4aProfileTests.ReviewedProfileHasExactReproducibleIdentity`, plan transport identity, runtime assembly hash |
| 2. Supported profile exercises actual Baize and HTTP stack | `Stage4aLoopbackTests.NormalRequest_UsesOneConnectionOneServerRequest`, all loopback probes |
| 3. Redirects/middleware cannot bypass guarded composition | `Redirect_IsNotFollowed`, `ApplicationRetryAboveGuard_IsCountedAndBoundedByRealGate` |
| 4. Unexpected application retries counted/bounded | same retry test: one admitted, one denied, one server request |
| 5. Internal transport retry observed/bounded | fault matrix plus v10.0.12 source audit; no replay branch for the selected shape |
| 6. Uncertain transport paths blocked | unreviewed/substituted eligibility tests; loopback remains blocked |
| 7. Budget reflects upper bound | `BudgetReservationScalesWithPhysicalAttemptBound`, admission-gate bound test |
| 8. Deadlines/cancellation correct | timeout, idle, read-cancellation probes |
| 9. Body disposal releases ownership | `FaultingBodyDisposalStillReleasesConcurrency` |
| 10. Substituted configuration invalidates eligibility | `SubstitutedTransportIdentity_InvalidatesEligibility`, runtime binding check |
| 11. Credentials behind admission | existing lease-aware provider tests plus refusal tests with sentinels |
| 12. Restart/recovery retain spent budget | `ReopenedBudgetLedgerRetainsReservedLiability`, Stage 2 recovery suite |
| 13. Synthetic contracts unchanged | full qualification suite; Stage 1–3 tests unchanged where historical behavior applies |
| 14. Evidence distinguishes observations | report fields, typed dispatch records, transport audit |
| 15. No live credentials/provider requests | final evidence commands; process deviation disclosed below |

## Gates

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Production source diff | Empty (`git diff -- src/`) |
| Full deterministic solution tests | 2,105 passed, 28 existing skips, 0 failures across 26 project/framework executions |
| Qualification tests | 292 passed, 0 failed |
| `dotnet format --verify-no-changes` | Only the three pre-existing `JsonSchemaGenerator.cs` findings |
| `git diff --check` | Clean |
| Live provider requests in final evidence commands | Zero |
| Live credentials / publication | Zero |

## Remaining restrictions

- Only `baize-sockets-http11-exact-v1` over the OpenAI-compatible adapter path is
  physically qualified. Claude, Gemini and Ollama live profiles remain
  unsupported for this bound.
- HTTP/2, HTTP/3, ALPN downgrade, GOAWAY stream replay and HTTP/2 stream-limit
  retry remain explicitly unsupported for live eligibility.
- Proxy configuration, redirect following, configured authentication, cookies,
  automatic decompression and custom middleware remain unsupported.
- Loopback profiles are test-only and never live-eligible.
- Wire-byte and preallocation bounds remain unresolved (S0-G6); only decoded
  byte ceilings are enforced.
- Provider billing semantics, token-completeness policy and account allowance
  must be separately reviewed for the selected Stage 4B provider/model.
- Transport qualification is not paid-execution authorization. A qualified plan
  still requires the operator's explicit live switch and the Stage 4B
  authorization.

## Process deviation

An intermediate Stage 3 privacy test still called `run --allow-live` for the
now-eligible OpenRouter profile before it was corrected. It used a sentinel
environment value, not a real credential, and exited through the failure path
without producing qualification evidence. The test now exercises a
still-ineligible profile. Because the request may have left the machine, this
stage does not claim zero outbound requests during development. No valid
credential or paid qualification run was intentionally made; the final evidence
commands and final test suite use no live profile execution.

## Next stage

Stage 4B — controlled real qualification — requires a separate explicit
authorization for one provider, model, endpoint/configuration, contract set,
dated request/token/cost allowance, approved secret reference and retained
evidence. It should use the Stage 4A profile and runner without broadening the
transport claim. No Stage 4B work, live provider call, push, merge or
publication is authorized by this handoff.
