# BQ-1b controlled execution: staged implementation plan

Date: 2026-10-25. Status: **PLAN FOR EXAMINATION; IMPLEMENTATION NOT AUTHORIZED.**
Repository: `jenolaszlo-sketch/penghou-baize`.

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

## Scope and reuse

Deliver the non-packable net10.0 CLI already proposed at
`eng/Penghou.Baize.Qualification/`, deterministic tests in the existing
`tests/Penghou.Baize.Qualification.Tests/`, and reviewed synthetic examples in
`eng/qualification/`. Ordinary output belongs in ignored
`artifacts/qualification/`. Use `System.Text.Json` and existing Baize packages;
no database, provider SDK, second router, host authority service or workflow
engine. No production API/package/dependency change is assumed by this plan.
If the seam audit proves one necessary, stop that stage and propose the smallest
separate change with its own tests rather than bypassing admission.

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
hard process-lifetime bound. No new workflow engine belongs in this stage.

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
| 1 — Registry and offline planner | Non-packable CLI shell; typed inventory/policy/resolved-plan; current descriptor registry and explicit Contracts friend assembly. | Two synthetic models resolve independently; unknown/empty selectors and all-excluded plans cannot yield a qualification success. Changed defaults/settings/build/fixtures change identity. Unknown authoritative values fail closed. `inventory validate` and `plan` perform zero secret reads, provider constructions and network calls. |
| 2 — Durable store and budget state | Minimum manifest, plan, append-only sequenced/hash-linked JSONL, exclusive OS lock, reservation/intent/result/settlement transitions and partial report. Depends on stage 1. | Create-new atomic files/flush-to-disk before consequential work. Inject failure immediately before/after each durability boundary. One final-slot reservation wins under contention. Corrupt committed prefix/duplicate conflict refuses resume; torn tail bytes are preserved and recovery appends a new segment. A second writer is denied. Unknown liabilities survive replay; no duplicate completed sends. |
| 3 — Existing semantic composition | One endpoint, isolated DI, no fallback/retry, wrapped model factory, deferred named secrets and a test execution adapter for V3. Depends on stages 1–2. | Denial causes zero secret reads/provider constructions; payload/context/target/attempt substitution rejects. Two fake subjects cannot share configuration or budget leases accidentally. Count exactly one semantic admission per intended call, not both the routed and inner seam; retain deliberate nested-call violations. Contracts/fixtures remain unchanged. |
| 4 — HTTP path audit and admission | Reviewed path manifest, injected HTTP guard and audited existing backend/handler composition; byte/concurrency/spacing limits. Depends on stages 2–3. | Final physical slot race admits one send. Hidden retries above the innermost guard are counted and denied at cap; retries below the observable boundary disable live eligibility. Destination/method substitution, redirects, unauthorized extra POST, oversized request/header/body/read and read-after-dispose reject. Lease lasts until disposal; denied requests cannot reach a recording terminal transport. No second router/HTTP executor. |
| 5 — Time, cancellation and usage settlement | Injectable clock, total/idle timers, bounded cleanup, complete/partial/unknown usage and fail-closed economics. Depends on stages 2–4. | Fake time proves header stalls, body stalls, metadata-only traffic, caller/deadline races and late callbacks cannot reopen admission. Include cancellation during secret lookup/serialization/send/read/disposal, absent/partial usage, over-bound usage and settlement replay. Null cost stays unknown; exhausted/expired/unbounded economics admits zero paid requests. |
| 6 — Local run/resume/report integration | Wire existing `run`, `resume`, `report` semantics and documented exit precedence to the accepted registry and durable store. Depends on stages 1–5. | Two fake models complete a frozen matrix; integrity shares each scenario's request. Crash-after-intent never auto-resends; completed failures stay completed. Changed build/config/contract fails resume before secrets. Broken stdout does not destroy durable results. Mixed/empty/partial runs cannot be green; every raw assertion and infrastructure cause survives reporting. Sentinel secrets/private payloads never appear in default files/progress/errors. |
| 7 — BQ-1b offline milestone handoff | Exact source/plan/store/test bindings, dispatch audit, residual risks, fake bundle and ordinary deterministic/API/dependency checks. | Repeatable offline fault suite and safe partial reports pass. Review the boundary audit independently before any later live use. No package publication, model catalogue approval, production routing mutation, scheduler or live demonstration is implied. |

### Fault injection matrix

Use recording fake semantic transports, existing real adapters over fake HTTP,
scripted response bodies, a virtual clock, faulting storage and sentinel secrets.
No paid/live failures are required. Every safety test observes both the guard's
result and downstream sends/secret reads/disposals; testing counters alone is
insufficient. Cover success, denial, two racing admissions, unexpected nested
calls/retries, connection loss before/after headers, partial body, malformed
terminal events, read/dispose hangs, missing/mismatched request IDs, reservation
and evidence I/O failures, torn journal writes and duplicate resume writers.
Retain original BQ-1a suites and both probe sources unchanged; a runner cannot
repair inputs, retry until green or reinterpret historical contract IDs.

## Examination decisions and deferred boundaries

| Decision to settle before the affected stage | Proposed bounded choice / consequence |
| --- | --- |
| Audited physical-send definition and allowed backend/adapter paths (stage 4) | Separate neutral calls, backend sends and wire attempts. Admit a real profile only when every billable attempt is conservatively bounded; otherwise ship offline-only eligibility. Examine any required existing-seam enhancement separately. |
| Input/reasoning bounds and economics sources (stage 5 / later live gate) | Fake profiles use known ceilings. Real paid profiles stay blocked without independently defensible bounds and a dedicated allowance; do not invent a tokenizer or cost estimate. Exact live cohort/account/rates remain separately authorized. |
| Hard cleanup/process lifetime (stage 5) | Five-second cooperative cleanup plus explicit incomplete state is the in-process claim. Examine a host termination/worker boundary if stronger guarantees are required; do not imply managed disposal can be forcibly cancelled. |
| Minimum evidence compatibility (stages 1–2) | Implement the existing v1 minimum record family/canonical writer and deterministic fixtures now. BQ-2 later formalizes schemas/projection without silently redefining BQ-1b records. |
| Policy defaults (plan examination) | Retain documented concurrency 1, retries 0, 10-minute run / 120-second attempt / 30-second idle / 5-second cleanup and the documented byte ceilings, tightened by actual component limits. Numeric request/token/cost allowances remain explicit per plan; no zero-as-unlimited. |

BQ-2 owns formal schemas, immutable projection/corrections, legacy imports,
cohort aggregation and approval/catalogue state. BQ-1b must still implement the
minimum durable envelope/recovery; it cannot defer crash safety to BQ-2.
BQ-3 owns versioned quirks and separately reviewed adapter fixes, including F1
and Ollama wire-ID forwarding. BQ-4 owns additional shared scenarios, complete
parts/continuation/parallel replay, repair-aware profiles, repetitions and drift.
BQ-5 owns shard exchange/workflow integration. GM-3/remaining GM-4/full GM-5
are not certified by this runner. No operational model capability claim, live
qualification, package publication or BQ-1b implementation starts with this plan.
