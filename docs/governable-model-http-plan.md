# Governable model and HTTP access: implementation roadmap

Updated 2026-10-04. GM-0 contract decisions and GM-1 implementation are recorded
in the [shared release checkpoint](../../Penghou/docs/model-http-release-handoff.md).
Public package delivery (GM-1P), Baize migration and Hufu integration remain open.
This is the active queue for this refactor. Streaming integrity and generation
roadmaps remain regression contracts for already implemented functionality.

Source: [the user's latest specification](proposals/2026-10-04-governable-model-http.txt),
preserved verbatim. The earlier sandbox-resident-parent experiment was completed
by Muse Spark and is outside this queue. Baize's refactor has no dependency on
repeating that experiment or on shipping a sandbox backend.

## Goal and completion boundary

Every externally consequential operation made through Baize crosses a replaceable
semantic model transport before provider execution. The host can inspect provider,
model, operation, neutral context and usage intent, deny execution, and observe
actual usage. Provider HTTP crosses an injected HTTP transport. Existing default
composition works without an authority system. Baize has no direct or transitive
dependency on Hufu.

`model.invoke` and `http.request` are separate capabilities. A host must be able
to allow a particular model and deny arbitrary HTTP simultaneously. Provider
HTTP is private to the trusted model implementation; a caller-supplied flag,
URL, credential profile or context ID cannot confer that trust.

This delivery covers seams, defaults, compatibility and qualification. Hufu
policies, persistent budget enforcement, vaulting, active stream revocation and
process/network isolation are separately owned follow-ups.

## Ownership and package direction

| Responsibility | Owner and dependency rule |
| --- | --- |
| Neutral model transport, context, usage and failures | `Penghou.Model.Abstractions`, in the Penghou repository; no Baize, Hufu, workflow engine or provider dependency |
| Neutral general HTTP transport | `Penghou.Http.Abstractions`, in Penghou, subject to existing web-contract inventory and compatibility review |
| Routing, model request construction, protocol parsing, retries and direct/default implementations | Baize and its existing provider packages; depend on neutral contracts |
| Optional authorization decorator | Hufu integration, preferably `Penghou.Hufu.Model` / `Penghou.Hufu.Http` when only generic contracts are required |
| Host identity, credential custody, budget service and broker deployment | Host-selected services; Hufu may govern access and reference reservations |
| Physical process/network isolation | External qualified provider, including Gagamba where supported |

The proposal's umbrella `Penghou.Abstractions` and adapter/package names are
illustrative. Capability-specific, product-neutral contract names preserve the
established Penghou architecture. A Baize-specific Hufu adapter is justified only
if its actual type dependencies require Baize. Baize never references that adapter.

Existing `Penghou.IO.Abstractions.IWebResourceReader` is bounded, credential-free
GET retrieval. It is not a general authenticated provider POST/streaming/upload
transport. Inventory its supporting types before designing HTTP contracts; reuse
equivalent types and document non-equivalence. Do not break the published IO
baseline or absorb this refactor into the completed resource correction.

## Contract decisions to close before package publication

1. Select the complete neutral type closure for model payloads, responses and
   stream events. The example `object Payload` is not the decided public API.
   Use bounded, versioned, typed data with explicit provider-extension rules;
   do not introduce a Penghou-to-Baize dependency or an opaque callback that can
   dispatch work outside the transport. Specify snapshots and collection limits.
2. Preserve streaming and asynchronous job lifecycles. Define invocation/open,
   submit, status, result retrieval and cancellation operations where supported,
   rather than flattening everything into a single response. Unsupported
   operations fail explicitly; embedding/reranking/moderation examples do not
   commit Baize to implementing new client families.
3. Preserve endpoint identity as well as provider/model identity. An
   OpenAI-compatible provider label does not identify the real destination.
   Bind host-selected endpoint/credential profiles and operation handles; reject
   substituted destinations, models and cross-context handles. Define whether
   redirect, upload and asset retrieval are part of the approved model operation
   or require a separate capability. Arbitrary caller URLs must not gain trust.
4. Define immutable, bounded correlation metadata and avoid collision with
   `System.Threading.ExecutionContext`. IDs are descriptive data, authenticated
   separately by the host. Baize preserves supplied context values through all
   attempts; decorators cannot silently replace authenticated bindings. Metadata
   is never automatically serialized to provider APIs.
5. Define estimated versus actual versus unknown usage, validated nonnegative
   token/cost values, currency and precision. Reconcile existing `LlmUsage` and
   generation usage without a second accounting ledger. Costs are estimates
   unless actually reported; partial/missing usage is not zero cost.
6. Define neutral authority denial and budget denial separately from provider
   authentication/403, throttling, availability and protocol errors. Specify the
   mapping to existing error/result APIs and preserve cancellation semantics.
7. Define HTTP request/body/header/response size limits, streaming ownership,
   disposal, cancellation and deadlines, redirects, decompression and credential
   handling. The example live `HttpContent` is not the decided shared contract.
   Preserve necessary multipart and streaming behavior through reviewed ownership
   contracts, without an unrestricted resource/process API.

## Ordered delivery and exit criteria

| ID | Owner | Work and required evidence |
| --- | --- | --- |
| GM-0 | Baize + Penghou | Inventory every provider call and indirect route; close the seven contract decisions above in an ADR with compatibility and protocol traces. Record the current source revision and baseline test results. |
| GM-1 | Penghou | Implement the approved neutral model and HTTP contracts, docs and API/dependency checks. Build, test and pack supported frameworks in CI; qualify isolated consumers and package dependency closure. Ready-to-publish contracts contain no implementation or product dependency. |
| GM-1P | Penghou / user release | Publish and verify indexed exact neutral package versions. Baize final adoption uses these NuGet packages; local feeds may qualify candidates but do not close adoption. |
| GM-2 | Baize core + chat providers | Route streaming chat and native completion through the semantic transport and provider HTTP through the injected HTTP transport; add replaceable DI defaults and retain constructor compatibility. Each actual retry/repair/fallback attempt crosses the final semantic boundary. |
| GM-3 | Baize batch + generation providers | Cover native batch submit/status/results/cancel, media generation/edit/status/result/cancel, upload and asset-retrieval paths. Preserve per-item context and current operation-handle semantics; a returned handle is not permission. No ungoverned built-in route remains. |
| GM-4 | Baize router + adapters | Preserve neutral context and usage through routing, fluent DI, custom provider/decorator hooks, Extensions.AI and batch/generation executors. Authority denial stops fallback by default; explicit alternative policy still requires a fresh check. |
| GM-5 | Baize | Pass B1-B12 plus the extra closure tests below and existing relevant streaming, provider, routing, batch, generation and diagnostics suites. Qualify packages and real consumers with exact published neutral dependencies in CI. |
| GM-6 | Baize / user release | Update README, provider/custom-provider guides, examples and compatibility notes; commit/push when requested, publish via existing release controls, verify indexed packages and fresh default/decorated consumers. |

GM-2 may be delivered as a chat-first development slice. It does not satisfy
"every invocation" until GM-3/4 close all built-in surfaces. Custom providers
retain extensibility, but a host may advertise governance only for configured
providers that use the seam; opaque custom implementations require explicit
coverage and cannot silently enter the governed profile.

Each stage's handoff records exact contracts/versions, supported operations,
uncovered paths, test evidence and the next gate. Do not mark contracts-only,
wrapper-only or local-feed work as the completed Baize refactor.

## Required acceptance tests

The source specification's B1-B12 are retained without renumbering:

| ID | Required proof |
| --- | --- |
| B1 | Default transport succeeds against a fake provider; ordinary apps require no Hufu. |
| B2 | Custom model transport replaces the default; no hidden provider connection occurs. |
| B3 | Decorator observes the complete neutral request before provider execution. |
| B4 | Denial performs zero provider dispatch and remains distinguishable. |
| B5 | Workflow/activity/agent/correlation IDs and metadata propagate unchanged. |
| B6 | Usage intent reaches the boundary unchanged. |
| B7 | Available actual usage and provider request ID survive response adaptation. |
| B8 | A stateful decorator allows one call, then denies the next after revocation; no cached permission. |
| B9 | Provider failures remain distinct and retain normal supported fallback behavior. |
| B10 | Explicit authority denial does not silently retry/fallback to another candidate. |
| B11 | Supplied HTTP transport receives every built-in provider HTTP operation in the declared coverage matrix. |
| B12 | Permanent architecture test checks direct and transitive package/project dependencies: no `Penghou.Hufu` or `Penghou.Hufu.*` in Baize's closure. |

Additional tests qualify what the examples leave implicit:

- Allow model invocation while denying generic HTTP; deny before credentials are
  resolved or any provider HTTP/upload is sent. Provider credentials remain
  host configuration, never public model-request data.
- Exercise actual provider retries, schema repair, router fallback and stream
  opens. Re-check each dispatch; denied streams emit no provider data. Opening
  is lazy where required, and cancellation/disposal releases owned resources.
- Cover all batch and generation lifecycle operations, including revoked status
  or result access and mixed-context items. Denying access does not undo a remote
  job already accepted; no automatic duplicate paid submission on response loss.
- Preserve partial/final usage, stream integrity and terminal errors. Distinguish
  unknown consumption after failure from unused reservation or zero usage.
- Prove endpoint/profile/handle substitutions fail in the configured governed
  composition and provider redirects cannot widen the approved destination.
- Inspect fixtures/logs: no secret headers, raw prompts or responses by default.
  Neutral invocation telemetry and correlation are separate from required Hufu
  evidence; telemetry failure cannot grant permission or change outcomes.

Tests use fake providers and deterministic HTTP fixtures. No paid/live provider
calls or credential access are required to qualify these seams.

## Separate follow-ups after the seam is stable

| ID | Owner | Gate |
| --- | --- | --- |
| HM-1 | Hufu optional model/HTTP integration | Implement supported authority vocabulary and current provider/model/operation/context checks in separate packages, with authenticated binding and mandatory evidence. Today's filesystem action profile does not already authorize models or generic HTTP. |
| HM-2 | Host + existing budget service + Hufu | Atomic reserve/settle references, concurrency, partial and unknown usage, idempotent reconciliation. No second accounting store inside Baize or Hufu. |
| HM-3 | Host credential broker | Authorization precedes credential resolution; profile references stay opaque; deny before loading secrets. Deploy the trusted broker outside an untrusted agent domain when containment is required. |
| HM-4 | Baize extension + host adapter | Optional candidate-availability filtering; advisory only, always followed by a fresh transport decision. |
| HM-5 | External isolation provider + host | Qualify credential-free, network-denied agent and allowed provider broker routes against direct sockets/HTTP/shell bypasses. An in-process decorator alone does not contain arbitrary code. |

Initial revocation blocks new invocations and new streams. An open stream may
finish or cancel normally. Active stream cancellation/drain and remote-job
termination need their own qualified semantics; do not promise them in GM-1/6.
Broader MCP/database/queue/Git/cloud authority is future capability work, not an
extra Baize implementation requirement.

The [source inventory](governable-model-http-inventory.md) records the initial
review and existing seams. Resume with the [handoff](governable-model-http-handoff.md).
