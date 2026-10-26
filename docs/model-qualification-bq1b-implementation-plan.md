# BQ-1b controlled execution: staged implementation plan

Date: 2026-10-26 (revised at Stage 4A). Status: **STAGES 0–3 COMPLETE;
STAGE 4A (PHYSICAL TRANSPORT QUALIFICATION) COMPLETE WITH RESTRICTIONS — ONE
HTTP/1.1 PROFILE HAS A REVIEWED PHYSICAL-ATTEMPT BOUND. STAGE 4B (LIVE RUNS)
NOT AUTHORIZED.** Repository: `jenolaszlo-sketch/penghou-baize`.

This refines the existing [architecture and roadmap](model-qualification-plan.md),
[twelve decisions](model-qualification-decisions.md),
[execution contract](model-qualification-execution.md),
[evidence specification](model-qualification-evidence.md) and
[workflow boundaries](model-qualification-workflow.md). Those specifications own
the policy; this document sequences implementation and identifies decisions to
examine. It creates no runner, inventory authorizing real endpoints, schedule,
spending allowance, transport implementation or capability claim.

Prerequisite: [BQ-1a ACCEPT WITH FOLLOW-UP](model-qualification-bq1a-closure.md).
Accepted qualification implementation:
`123dd86249eba57be700d5e32b486a7527e06474`; handoff/evidence:
`7c986ef0964b58c6d3d18753eb3e3c43ff1ccb64`. Pin the actual integrated main
commit at BQ-1b kickoff; do not assume a branch name is an immutable build.
The current contracts are `chat.baseline@3`, `tools.single@3` and
`chat.streaming-integrity@2`, with their unchanged version-1 request fixtures.
Earlier V2/streaming-1 evidence is historical and remains reproducible.

## Stage 0 feasibility result

Starting main: `3986dde792f6af49f4f3a51e27db1a6d721d691f`.
The [bounded Stage 0 audit](model-qualification-bq1b-stage0-audit.md) and
[five execution decisions](model-qualification-bq1b-stage0-decisions.md) establish
the implementation basis. Twenty-six deterministic probes demonstrate existing
ports and counterexamples without production edits or real network requests.

**Stage 1 is technically ready after explicit implementation authorization.**
No production API change is needed for offline Stages 1–3. The initial milestone
is offline/two-fake-model execution; real network profiles remain ineligible
where required wire-attempt, resource or economic bounds are unproven. No new
BQ-1a review or broader GM milestone implementation is required.

Carry these constraints into the stages below:

- Use metadata resolution for planning, never runtime endpoint validation,
  which resolves secrets/constructs clients outside semantic admission.
- Freeze an existing injectable fixed options monitor and all subject/protocol
  data; default routing reloads configuration. Use one endpoint and one semantic
  guard at the deferred boundary, without fallback or duplicate inner guards.
- Count neutral calls and terminal application/backend sends separately; neither
  establishes a wire-attempt count. Handler retries and destination substitution
  require a last application guard, and runtime-internal retries remain a live
  eligibility gap. No universal four-attempt reservation multiplier is approved.
- Treat partial/derived totals and absent reasoning/cost fields as insufficient
  settlement evidence; observe full semantic usage without changing V3 contracts.
- Adopt cooperative cleanup with five-second grace. Secret resolution may
  outlive a cancelled waiter and DisposeAsync may block synchronously; revoke
  leases, recheck late secret results and schedule cleanup off the coordinator.
- Separate decoded/delivered byte limits from wire/preallocation claims and
  keep profiles ineligible for stronger guarantees the existing path cannot meet.

The audit's **S0-G1–S0-G7** table names the smallest correction, affected
components, required tests and owning stage for every gap. These are runner
composition/eligibility/minimum-evidence requirements, not blanket approval for
production changes. If a genuine lower wire gate is later demanded, propose a
narrow existing-seam change separately; do not create another HTTP stack.

## Scope and reuse

Deliver the non-packable net10.0 CLI already proposed at
`eng/Penghou.Baize.Qualification/`, deterministic tests in the existing
`tests/Penghou.Baize.Qualification.Tests/`, and reviewed synthetic examples in
`eng/qualification/`. Ordinary output belongs in ignored
`artifacts/qualification/`. Use `System.Text.Json` and existing Baize packages.

The execution architecture reuses the Penghou components that already exist
instead of building duplicates of them:

- **Penghou.Zhinu** (pinned published `0.2.0-preview.4`) owns the durable
  workflow lifecycle: step boundaries, committed step-result reuse, retries,
  leases, cancellation, restart recovery and inspection. Baize must not build
  a second workflow state machine.
- **Penghou.Siming** (pinned published stable `1.0.0`,
  `Penghou.Siming.Sqlite`) owns the append-only, hash-chained, verifiable
  evidence ledger, its idempotent append semantics, checkpoints and
  verification. Baize must not build a second JSONL journal or hash chain.
- **Baize** owns what only it can: model configuration resolution, the routed
  adapter stream, qualification contracts/fixtures, budget policy and the
  qualification evidence records.

This replaces the custom durable store, sequenced JSONL journal, exclusive
OS lock writer, torn-tail recovery and workflow state machine that earlier
revisions of this plan assigned to Baize-owned Stages 2–6. Requirements whose
only purpose was implementing that custom machinery are removed; the safety
boundaries they protected (immutable plans, durable reservation before
dispatch, conservative accounting, preserved failures, restart without
replenishment, fail-closed eligibility) remain unchanged and are now carried
by the pinned components plus the integration logic below.

Zhinu and Siming require no distributed transaction. A workflow failure does
not roll back historical evidence: failures are observations worth
preserving. Zhinu's at-least-once execution is accepted; interrupted external
operations may be repeated under the applicable execution policy, provided
each actual attempt is separately identified, reserved and recorded. If
Zhinu commits a step but the following Siming append fails, reconciliation
re-appends with Siming's idempotent keys; this is not turned into a
distributed transaction protocol.

No production API/package/dependency change is assumed by this plan except
the two pinned tool-side package references. If the seam audit proves a
production change necessary, stop that stage and propose the smallest
separate change with its own tests rather than bypassing admission.

Component pinning (recorded in every run manifest and the Stage 2 handoff):

| Component | Pinned published package | Purpose in BQ-1b |
| --- | --- | --- |
| Penghou.Zhinu | `Penghou.Zhinu` + `Penghou.Zhinu.Sqlite` `0.2.0-preview.4` | Durable per-case workflow, stable run identity, step lifecycle, cancellation, restart/inspection |
| Penghou.Siming | `Penghou.Siming` + `Penghou.Siming.Sqlite` `1.0.0` | Append-only verifiable qualification evidence ledger, idempotent appends, checkpoints, budget CAS |

These packages are tool-only dependencies of the `eng/` CLI; the production
dependency closures remain exactly the previously verified 12 packages.


| Existing component, inspected at BQ-1a tip | Reuse / qualification responsibility |
| --- | --- |
| `QualificationScenariosV3`, `QualificationContractsV3`, shared fixtures | Invoke directly; add only an explicit friend assembly for the CLI when implementing. Do not copy assertions, prompts, repair/schema validation or invent a permissive execution variant. |
| `LlmEndpointOptions`, profiles/settings, provider registry and `AddLlmRouting` | Resolve existing configuration semantics, isolate one endpoint/model per case, no fallback and `MaximumAttempts=1`. No production route/profile write-back. |
| `IBaizeModelTransportFactory`, `DeferredLlmClient` | Wrap the routed semantic boundary; durable reservation and immutable binding checks must precede deferred secret resolution/provider construction. Avoid a second reservation if an inner provider also exposes a semantic seam. |
| DI-injected `IHttpTransport`, `BaizeHttp.CreateClientFactory`, `BaizeHttpTransport` | Admission/counting and tighter existing limits around the supported HTTP path. Reuse the existing named-client backend and owned body, not another HTTP stack. |
| `AddBaizeTransport` / `BaizeHttp.ClientName` | Existing defaults disable redirects/cookies. Compose a dedicated, audited handler chain; disallow ambient/custom retry handlers and credentials. Defaults alone are not proof that every physical retry is observable. |
| `ISecretProvider` and deferred endpoint clients | Resolve named references only after admission; never invoke `LiveEnvironment`, read ancestor dotenv files, accept raw keys on CLI or persist secret values/hashes. |
| Existing transport/router/context/diagnostics tests | Extend boundary fixtures and reuse known denial, request-ID, ownership and redaction expectations. Do not replace them with field-serialization-only tests. |
| Current Activity/router diagnostics and `ContractResult` assertions | Preserve as separate observations; they do not count physical sends, establish wire provenance or prove remote cancellation/billing stopped. |

Core source anchors are `src/Penghou.Baize/Transport/`,
`src/Penghou.Baize/BaizeHttp.cs`, `BaizeServiceCollectionExtensions.cs`, and
`src/Penghou.Baize.Router/{DeferredEndpointClients.cs,Extensions/ServiceCollectionExtensions.cs}`.
Relevant existing tests include `ModelTransportQualificationTests`,
`BaizeHttpTransportAdoptionTests`, `HttpTransportLifecycleTests`,
`HttpTransportFailureTests`, `RoutedModelTransportTests` and `TransportContextTests`.

## Invariants to examine before coding

### Immutable subject and contract binding

Freeze reviewed inventory/policy, explicit selectors, exclusions, hosting service,
account-scope reference, provider key, wire model, adapter assembly/package/content
digest, dependency closure, endpoint origin/port/base path, protocol/API revision,
dialect/settings, effective capabilities/defaults, safe request parameters,
timeouts, economics and allowance sources/expiry. Preserve requested versus
resolved identity and revisions independently. A mutable model alias or unknown
protocol revision stays unknown; never manufacture a weight/revision identity.
Ollama remains applicability Undetermined / provenance NotObserved unless a
separately reviewed binding and evidence establish more. Gemini normalized IDs
and collector-assigned IDs never become provider provenance.

Use one versioned canonical writer: ordinal keys/identifiers, explicit defaults,
ordered sequences preserved, decimal-string money, deterministic hashes. Store
the frozen plan once; resume rejects changes to subject, contract/fixture IDs,
assertion/code/assembly hashes, policy or runner build. Secret references identify
slots, not credential values or proven account identity; a nonsecret account/key
revision reference may narrow scope, otherwise mark it unknown. Never hash raw
credentials into subject identity. Synthetic fixture content can be hashed;
private text cannot be made safe merely by hashing it.

The CLI explicitly allowlists the three current descriptors and their build
digests. The integrity descriptor observes the same dispatch as baseline/tools;
it does not spend a second request. Historical IDs are available only to offline
evidence readers/replays, not silently selected for a current run. Plan records
exact descriptor assertions, fixtures, maximum dispatches and the 512 output cap.
Do not infer support from probe capability flags or a successful aggregate bool.

### Admission, physical sends and economics

Maintain distinct IDs/counters for case/trial, semantic invocation and physical
HTTP request attempt. Under one coordinator, commit worst-case token/cost
reservation before semantic admission and deferred credential access. Validate
endpoint eligibility at that point. Before an actual send, commit dispatch
intent and atomically consume its HTTP slot, recheck destination/method, bytes,
deadline and concurrency. Hold the concurrency lease through body disposal.
Reject extra semantic calls and unaccounted inference POSTs, including retries.

The current legacy HTTP adapter path supplies no semantic `HttpRequestContext`
automatically. Bridge it with a runner-owned invocation-scoped lease/correlation
scope created during semantic admission and closed on completion/cancellation;
caller-provided IDs or headers are not admission authority. Map each generated
HTTP request ID to that lease, reject late/background sends after closure and
test concurrent scope isolation. Do not introduce a global current-model variable
or silently assume the transport packages already propagate the binding. If
the existing async flow cannot preserve it reliably, treat that path as an
explicit seam gap before live eligibility.

An `IHttpTransport.SendAsync` observation alone cannot prove one network attempt:
a wrapped HttpClient handler may retry below it. Audit every allowed adapter and
the complete named-client chain, place request admission/counting at the last
application-controlled boundary before the backend, and test retries above that
guard. Disable redirects, automatic authentication/cookie flows and configured
retry middleware. Distinguish neutral-transport calls, admitted backend sends
and backend-internal wire attempts in evidence. If backend-internal replay cannot
be bounded or observed, **block live admission for that profile**; do not relabel
the outer count as a verified physical-wire count. Any required narrower seam is
a separately examined gap, not permission to build another HTTP stack or claim
full GM-4/GM-5 closure. BQ-1b can complete its offline milestone with the gap
explicit and the real path disabled.

Positive finite limits narrow globally, per account, per model and per case:
semantic/HTTP calls, input/output/total tokens, request/response/header/read/event/
evidence bytes, currency/cost, concurrency, spacing and elapsed time. Tighten
existing neutral HTTP limits; preserve body ownership and count decoded bytes.
Audit serialization/allocation before the guard: a response-byte limit is not
a guarantee against an oversized pre-normalized event or buffer. If event size
cannot be bounded before materialization, document the resource limit honestly
and gate unsupported paths; no arbitrary-chunk progress assertion proves it.

Use exact token counts or reviewed conservative provider-specific bounds that
include templates, schemas, reasoning and conversation growth. Characters/4 is
not a hard cap. Missing token bounds, price/allowance source, currency or billable
reasoning semantics block paid admission. Unknown is null/tagged unknown, never
zero/free/unlimited. Fake profiles provide deterministic known bounds for tests.
An explicitly verified no-charge trial still needs its own dated, expiring
request/token allowance. V1 uses zero automatic availability retries and the
existing plan's concurrency-one defaults; future retries require a new envelope
and retain the original failure.

Settle once from trustworthy complete actual usage or proven no-dispatch.
Missing/partial usage, response loss, cancellation or unresolved intent retains
the full conservative reservation as `spentUnknown`. Above-bound usage halts new
admission and invalidates the bound policy. Resume cannot replenish allowance,
release ambiguous liability or resend submitted work. External account spending
requires an operator-reserved allowance/host allocator; this local ledger does
not claim to see unrelated clients or provide a provider billing guarantee.

### Deadlines, observations and cleanup

Use monotonic elapsed time via injectable `TimeProvider`, with separately recorded
operator, run-total, attempt-total and idle signals. Plan effective timeouts,
including any tighter Baize/HttpClient ceiling. Total time includes credential
resolution, serialization, headers and body reads. Idle monitoring must observe
blocked body reads and define progress explicitly (positive bytes, not empty
reads); heartbeats cannot extend the absolute deadline. Bind the contract's
explicit `Deadline` token without losing the original cancellation cause.
Foreign/bare OCE remains ambiguous, not an invented timeout. Preserve R1's last
explicit completion value through metadata-only records.

On any stop, close admission first, cancel linked operations, dispose stream/body
and scope, and attempt terminal/partial evidence within the existing five-second
cleanup grace. A hung disposal cannot hold admission open or be reported as
successful cleanup. Retain unknown liability and record unfinished cleanup.
Cooperative in-process cancellation cannot force arbitrary code or remote compute
to stop; an external host may terminate a stuck process after deadline+grace.
Whether a local worker process is needed must be examined before promising a
hard process-lifetime bound. No workflow engine is built here: the pinned
Zhinu engine owns workflow state; Baize owns only the qualification steps.

Keep infrastructure status, raw contract assertions and attribution distinct.
Host/budget denial, I/O corruption, timeout, cancellation, credentials/access,
generic 400/404, missing usage and unobserved transport paths cannot establish
unsupported model capabilities. Preserve failed observed assertions even when
an attempt is Blocked/Inconclusive; final-dependent checks remain NotObserved.
Known F1 matches a preserved deterministic adapter reproduction; never silently
turn its sequence failure into Unsupported or waive it in the runner. Unknown
causes remain unresolved. BQ-2 owns catalogue-level Unsupported/Verified policy.

Default evidence is a safe typed allowlist: exact build/config/plan hashes, opaque
IDs, bounded numeric counters/statuses, cause/stage codes, assertion IDs, usage
source/uncertainty and references. Exclude raw bodies, prompts, tool arguments,
credentials, URLs with private material and exception strings. Raw capture stays
off; existing Diagnostics capture is not copied from `LiveClientFactory` defaults.
If evidence capacity or durable writes fail, deny new work rather than dropping
failed assertions or producing a green partial run.

## Staged implementation and acceptance gates

The stages below are proposed work, not authorizations. Examine this plan first;
after authorization, each stage produces a bounded diff and offline evidence.

| Stage | Deliverable / dependency | Concrete acceptance criteria |
| --- | --- | --- |
| 0 — Execution boundary feasibility (complete) | Audit exact starting main; standalone fake-HTTP probes; decisions D1–D5; no production or runner implementation. | READY WITH RESTRICTIONS: 26 probe invariants plus existing boundary/qualification tests; Stage 1 metadata planning feasible, actual wire coverage/economics unknowns explicitly deny live eligibility. See linked audit and S0-G1–G7 ownership. |
| 1 — Registry and offline planner (complete) | Non-packable CLI shell; typed inventory/policy/resolved-plan; current descriptor registry and explicit Contracts friend assembly. | Two synthetic models resolve independently; unknown/empty selectors and all-excluded plans cannot yield a qualification success. Changed defaults/settings/build/fixtures change identity. Unknown authoritative values fail closed. `inventory validate` and `plan` perform zero secret reads, provider constructions and network calls. |
| 2 — Durable qualification execution slice (this stage) | Frozen-plan verification and binding; Zhinu workflow per selected case/trial with stable identities; deterministic synthetic fake transport behind the real Baize routed adapter; BQ-1a V3 scenario execution; Siming evidence ledger with idempotent appends; conservative budget accounting; reconciliation; offline JSON/Markdown report; `run`/`resume`/`report` commands. Depends on stage 1 plus pinned Zhinu/Siming. | Frozen plan → Zhinu workflow → Baize synthetic invocation → BQ-1a assertions → Siming evidence → qualification report works end to end with persisted state. Deterministic fault/restart suite covers success, contract failure with preserved negatives, cancellation, restart after a committed step, failure between workflow completion and Siming append, repeated append identity, crash-after-intent re-execution as a distinct attempt, evidence-storage failure producing no green report, budget exhaustion/unknown usage, and multi-case isolation. No real network, credential or paid execution. |
| 3 — Real-transport readiness (complete with restrictions) | Admission guard at the last application boundary beneath the real Baize neutral transport; attempt lease bridging the legacy adapter path; lease-aware credential resolution; per-dispatch HTTP accounting and denial at cap; request/response byte ceilings; concurrency one held through body disposal; total and idle deadlines; fail-closed live-eligibility assessment and `--allow-live` gate; cost reservations; compatibility attribution (failure stage + suggested class, F1 fixture-bound); loopback integration over the real sockets stack. | Deterministic suite proves the guarded composition, blocked configurations with zero secret reads, dispatch-cap denial, concurrency one, hidden-retry counting/denial, size violations, idle deadlines, lease-gated admission, identity-consistent evidence, recovery without fabricated success, provider-vs-contract distinction, F1 reproduction, and privacy. Real profiles remain live-blocked on `wire-attempt-bound-unproven` (S0-G1); disposition READY WITH RESTRICTIONS. |
| 4A — Physical transport qualification (complete with restrictions) | Runtime/source audit of the actual `SocketsHttpHandler` path; one exact HTTP/1.1 pinned profile; guard protocol pin and request-shape restrictions; physical-attempt bound enforcement per attempt; budget reservation scaled by the bound; deterministic loopback fault matrix; eligibility and evidence binding to the profile identity. | One admitted OpenAI-compatible inference request cannot produce more than one physical HTTP request attempt under `baize-sockets-http11-exact-v1`. HTTP/2/3, proxy, redirect, configured authentication, cookie, decompression and custom-middleware paths remain blocked. Loopback only; no live request. Disposition QUALIFIED WITH RESTRICTIONS. |
| 4B — Controlled real qualification | Explicitly authorized live runs of an eligible plan through the guarded composition; predeclared dated allowances; per-quirk findings and adapter defects recorded. Depends on Stage 4A, an operator authorization decision, a reviewed provider/model/endpoint, and provider billing semantics. | Actionable compatibility findings with exact configuration, contract, attempt and evidence identity; F1-style adapter defects stay separately attributed; provider failures never become model capability failures. No automatic promotion or scheduling. |
| 5 — BQ-1b qualification milestone handoff | Exact source/plan/store/test bindings, dispatch audit, residual risks, fake bundle, real-bundle (if authorized), ordinary deterministic/API/dependency checks. | Repeatable offline fault suite and safe partial reports pass. Review the boundary audit independently before any broader live use. No package publication, model catalogue approval, production routing mutation, scheduler or unattended promotion is implied. |

### Stage 4A result

The [transport audit](model-qualification-bq1b-stage4a-transport-audit.md)
selects one profile, `baize-sockets-http11-exact-v1`, and establishes an upper
bound of one physical HTTP request attempt per admitted inference invocation.
The profile pins HTTP/1.1 exact, disables redirects/authentication/cookies/proxy/
decompression, rejects contentless requests and `Expect: 100-continue`, records
the runtime and `System.Net.Http` assembly hash, and is enforced at the admission
gate and in the budget reservation. The [Stage 4A handoff](model-qualification-bq1b-stage4a-handoff.md)
and [evidence bundle](reviews/bq1b-stage4a-evidence/README.md) bind the exact
build, probe matrix and remaining restrictions.

Only this profile earns removal of `wire-attempt-bound-unproven`. Unreviewed
profiles keep it. Loopback profiles use the same handler but remain `Blocked`
with `loopback-not-live-eligible`. HTTP/2/3, proxy, redirect, configured
authentication, cookie, decompression and custom-handler paths remain explicitly
unsupported. Provider billing semantics and the actual endpoint/provider/model
selection are Stage 4B facts; transport qualification is not paid-execution
authorization.

### Fault injection matrix

Stage 2 uses the real pinned Zhinu/Sime stores over local files (never mocks
of the two components), a recording fake terminal HTTP transport behind the
real Baize routed adapter, scripted deterministic SSE responses, faulting
evidence seams and sentinel secrets. No paid/live failures are required.
Every safety test observes both the guard's result and downstream sends/
secret reads/disposals; testing counters alone is insufficient.

Required deterministic scenarios: success; contract failure with preserved
negative assertions; cancellation and incomplete observation; process restart
after a committed Zhinu step; failure between workflow completion and the
Siming append; repeated Siming append with the same event identity; a
legitimate re-execution represented as a distinct actual attempt; evidence
storage failure that cannot produce a successful qualification report; budget
exhaustion and unknown usage; multiple selected synthetic cases without
identity or budget contamination. Retain original BQ-1a suites and both probe
sources unchanged; a runner cannot repair inputs, retry until green or
reinterpret historical contract IDs.

## Stage 0 decisions and deferred boundaries

The linked Stage 0 decisions select the bounded initial claims below. These
choices resolve feasibility without authorizing implementation or any live run.

| Decision applied by the affected stage | Selected initial choice / consequence |
| --- | --- |
| D1/D5 — physical-send definition and allowed paths (stages 1/3) | Distinct semantic/neutral/backend/wire quantities; wire attempts unknown without evidence. Fake backend milestone supported, real profiles blocked when hard coverage/bounds are missing. Any required lower seam is a separate scoped decision. |
| D2 — input/reasoning bounds and economics sources (stage 3 / later live gate) | Fake profiles use known ceilings. Missing bounds/rates/currency/allowance block paid admission; partial/derived usage retains full reservation. Exact live cohort/account/rates remain separately authorized. |
| D3 — cleanup/process lifetime (stage 3) | Five-second cooperative cleanup with explicit incomplete state, lease revocation and retained unknown liabilities. Hard worker/process lifetime is a separate later decision, not a prerequisite to offline Stage 1. |
| D4 — minimum evidence compatibility (stage 2) | v1 minimum evidence envelope carried by the pinned Siming ledger: reservation before consequential calls, exact boundary kind, idempotent append identities, unknown spend, verifiable hash chain and crash-safe replay. BQ-2 later formalizes schemas/projection without silently redefining BQ-1b records. The workflow lifecycle is carried by Zhinu, not a Baize-owned state machine. |
| Policy defaults (plan examination) | Retain documented concurrency 1, retries 0, 10-minute run / 120-second attempt / 30-second idle / 5-second cleanup and the documented byte ceilings, tightened by actual component limits. Numeric request/token/cost allowances remain explicit per plan; no zero-as-unlimited. |

BQ-2 owns formal schemas, immutable projection/corrections, legacy imports,
cohort aggregation and approval/catalogue state. BQ-1b must still implement the
minimum durable envelope/recovery through the pinned Siming/Zhinu components;
it cannot defer crash safety to BQ-2.
BQ-3 owns versioned quirks and separately reviewed adapter fixes, including F1
and Ollama wire-ID forwarding. BQ-4 owns additional shared scenarios, complete
parts/continuation/parallel replay, repair-aware profiles, repetitions and drift.
BQ-5 owns shard exchange/integration on top of the Zhinu runs. GM-3/remaining
GM-4/full GM-5 are not certified by this runner. No operational model capability
claim, live qualification, package publication or BQ-1b implementation starts
with this plan.
