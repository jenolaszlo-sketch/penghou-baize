# BQ-1b Stage 3 handoff — real transport readiness and API compatibility qualification

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Disposition: READY WITH RESTRICTIONS.** The qualification framework can now
prepare an explicitly configured real provider/model combination for testing,
exercise its actual Baize adapter over a controlled guarded transport, and
produce an evidence-backed compatibility report. Synthetic execution and the
real-adapter composition are fully verified; real network execution remains
live-blocked on exactly one honest, evidence-bound gap
(`wire-attempt-bound-unproven`, Stage 0 S0-G1) plus per-profile economics and
allowance requirements. No live provider call, secret read or publication
occurred.

| Binding | Value |
| --- | --- |
| Base (Stage 2 accepted) | `f3afe082ca7de58bf856220d3b9cd5760391481f` |
| Implementation commit | see Stage 3 commits below |
| Dedicated worktree | `Penghou.Baize.stage3` on branch `implementation/baize-bq1b-stage3` |
| Evidence bundle | [bq1b-stage3-evidence](reviews/bq1b-stage3-evidence/README.md) |
| Contract backlog | [model-qualification-bq1b-contract-backlog.md](model-qualification-bq1b-contract-backlog.md) |

`git diff -- src/` is empty: zero production source changes. Stage 1 and
Stage 2 suites, evidence and fixtures remain unchanged and passing.

## What Stage 3 delivered

**Guarded transport composition (runner-owned, `eng/`).** Every provider
request crosses `QualificationAdmissionHandler` — the last application
boundary immediately before the terminal handler, beneath the real Baize
neutral transport, router and adapters:

- destination/method identity against the frozen plan binding
  (`http-destination-substitution` denies substitution),
- the invocation-scoped attempt lease (an `AsyncLocal` bridge for the legacy
  adapter path that carries no `HttpRequestContext`; admission without a
  lease is denied),
- request byte ceilings, concurrency one held until response-body disposal
  (a released-slot regression was caught and fixed by the concurrency test),
- per-dispatch budget admission against the Siming budget stream with
  `QualificationHttpDispatchAdmitted`/`QualificationHttpDispatchResult`/
  `QualificationHttpDispatchDenied` evidence — hidden retries above the guard
  are counted and denied at cap; retries below the guard remain unobservable
  and are recorded as the eligibility blocker, never as a proven wire count,
- decoded response byte ceilings, the absolute attempt deadline and a
  progress-based idle deadline inside the response-stream wrapper,
- pinned real terminal (`SocketsHttpHandler`, no redirects/cookies) for live
  profiles; recording fake terminal for synthetic; loopback verification
  over the real sockets stack.

**Credential ordering.** `LeaseAwareSecretProvider` resolves environment
credentials only inside a live attempt lease, rechecking before and after
the asynchronous lookup; synthetic subjects resolve only their exact frozen
slot. Refused or ineligible runs read zero secrets (asserted with sentinel
environment values).

**Fail-closed live eligibility.** `run --allow-live` gates real profiles;
each selected case gets an evidence-bound `QualificationEligibilityAssessed`
record. Critical reasons (allowance unverified, token/cost bounds missing,
credential reference missing, endpoint not allowed, wire-attempt bound
unproven) refuse with a `run-refused` JSON listing reasons; limitations are
recorded separately. Eligibility is evidence, never authorization: an
eligible plan still requires the explicit operator switch, and no profile is
eligible in this stage because the runtime-internal wire multiplicity below
a real sockets terminal stays unobservable.

**Budget completion.** Cost units reserve against the policy cost ceiling
when the profile declares paid economics; unknown economics stay limitations
with the allowance gate. Unknown usage and unknown cost retain full
conservative liability (unchanged). Zero automatic retries (unchanged).

**Compatibility reporting.** Reports now carry hosting provider, protocol
and version, adapter, settings, contract/fixture versions, eligibility state
with reasons, per-assertion outcomes, failure stage
(admission/reservation/transport-response/transport-lifecycle/
protocol-decoding/assertion), a suggested attribution class
(`baize-adapter-defect` / `provider-api-difference` /
`model-capability-limitation` / `configuration-requirement` /
`infrastructure-failure` / `unverified`), usage/cost uncertainty, HTTP
dispatch records and evidence references. Raw assertion outcomes are stored
separately from attribution; unresolved attribution stays `unverified`.

**Preserved F1.** The co-terminal tool-delta reproduction now lives as a
deterministic synthetic fixture (`synthetic/model-coterminal`); the strict
streaming assertion keeps failing, and the evidence records
`baize-adapter-defect` with `deterministic-fixture` basis — never waived.

## Acceptance coverage (15/15 deterministic tests)

`Stage3TransportTests`, `Stage3EligibilityTests`, `Stage3AttributionTests`,
`Stage3LoopbackTests`, `Stage3PrivacyTests` (24 tests) prove: the eligible
synthetic composition reaching the real adapter and guard; blocked
configurations with zero credential resolution or dispatch; request-cap
denial at the guard; concurrency one under four racing sends; hidden-retry
counting and denial at cap; response-size and idle-deadline enforcement;
lease-gated admission and destination substitution; identity-consistent
evidence across reservation/intent/dispatch/finished; Zhinu recovery without
fabricated success; provider failures distinct from contract failures;
F1 reproducibility with adapter attribution; privacy byte-scans; the real
sockets loopback path; and lease-aware secret denial outside the lease.
Details and hashes: [evidence bundle](reviews/bq1b-stage3-evidence/README.md).

## Gates

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings / 0 errors |
| Production source diff | Empty |
| Full deterministic solution tests | 2,077 passed, 28 existing skips, 0 failures (2,105 total) |
| Production dependency closures | 12/12 verified, exact public transport packages, no Hufu |
| `dotnet format --verify-no-changes` | Only the three pre-existing `JsonSchemaGenerator.cs` findings; new files format-clean |
| `git diff --check` | Clean |
| Live requests / secret reads / publication | Zero |

One transient observation, recorded for reproducibility: during the first
full parallel solution run a single test failed and passed in isolation and
in the two complete reruns (the load-sensitive SQLite contention category;
the budget CAS loop additionally retries SQLite busy conditions after this
was observed). No source related to that test changed between the runs.

## Remaining limitations

- Real network profiles stay live-blocked on `wire-attempt-bound-unproven`
  (unobservable runtime-internal replay below the sockets terminal). A
  separately reviewed lower-bound decision per profile is required before
  any controlled live run; the runner must not invent that guarantee.
- Event-level preallocation bounds and hard process-lifetime enforcement
  remain unproven (S0-G6); the runner enforces decoded-byte ceilings and
  cooperative deadlines only.
- Only `openai-chat-completions` has synthetic terminal coverage; Claude,
  Gemini and Ollama profiles plan and refuse correctly but have no scripted
  backend yet.
- Stage 1 historical plans remain non-executable by the Stage 3 runner (the
  runner build closure gained the pinned components); plans are regenerated
  with the current build by design.

## Next stage

Stage 4 — controlled real qualification — requires explicit operator
authorization, a reviewed per-profile wire-attempt acceptance, dated
allowances, and uses exactly this runner. Not started here.
