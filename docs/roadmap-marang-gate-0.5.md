# Marang Gate 0.5 handoff

Status: audited 2026-09-04 against the local `0.3.0-preview.5` source state.

Future contextual reputation, economics, and model-usage guidance are tracked
separately in
[`roadmap-experience-signals.md`](roadmap-experience-signals.md). Gate 0.5 does
not require or imply experience-informed automatic routing.

This note records what Marang can consume from Baize and where a semantic gap
must be closed. It does not make Baize an agent runtime or durable workflow
engine. Ordinary chat completion is a request/stream surface; the generation
and native-batch surfaces are the only Baize APIs that currently expose
provider operation handles.

## Reusable now

| Marang need | Baize surface and evidence | Integration disposition |
| --- | --- | --- |
| Model request/response | `ILlmClient.StreamAsync`, optional `ILlmCompletionClient.CompleteAsync`, immutable `LlmRequest`/`LlmMessage`, endpoint capabilities and request validation | Use in a provider adapter for one bounded inference. Marang supplies profile, budget, context, and policy; Baize supplies transport and capability checks. |
| Model/tool output | `LlmResponse`, ordered `LlmContentPart`s, `LlmToolCall`/`LlmToolResult`, finish-reason classification, and `LlmClientFailureKind`/`LlmClientException` | Use as the raw normalized input to a Marang worker receipt. Tool execution, authorization, side effects, and retries remain host/Marang policy. |
| Malformed structured/tool output | `Penghou.Baize.Tools` repair decorator, `LlmResponseNormalizer`, pseudo-call extraction, repair diagnostics, and `LlmToolResultParserBase`; tests cover malformed JSON, truncation, duplicate JSON properties, and preservation of unknown/empty/invalid calls | Use repair as a deterministic recovery step, but accept only a separately validated result. Do not treat repair success as semantic correctness. |
| Usage and provider evidence | `LlmUsage`, `LlmProviderDiagnostics`, `LlmClientMetadata`, router diagnostics, rate-limit information, and opt-in Baize telemetry | Map provider/model/endpoint/response ID/usage into Marang evidence. Marang must add its own invocation, profile, node-generation, and input-artifact identities. |
| Handle-based asynchronous work | `IBaizeBatchClient` has serializable `ProviderBatchHandle`, status, result retrieval, and cancellation; `IGenerationClient` has pinned `GenerationOperationHandle`, idempotency, status/cancel, and `IGenerationExecutor.WaitAsync` | Reuse only for native provider batches or artifact generation where the activity has that lifecycle. Generation is not a model-completion contract. |
| Local cancellation and polling bounds | Cancellation tokens flow through HTTP/streaming and generation executor polling; generation has validated timeout/backoff options and reports resumable timeout handles | Treat token cancellation as local cancellation. Treat generation timeout as “may still run”; persist the handle and re-observe. Do not claim that canceling a chat token cancels provider work. |
| Default transport hygiene | `LlmJson.FormatForError` bounds error text and strips URL query/fragment; diagnostics avoid payloads by default; API-key handling is transport-internal | Retain these defaults. Marang still owns workspace/credential policy and evidence redaction. |

## Upstream Baize work before Marang relies on the contract

These are reusable Baize semantics, not Marang-specific orchestration.

### P0 — closed: the already-planned tool integrity gaps

1. Blank and duplicate tool declarations are rejected at the earliest common
   request boundary (`LlmToolDeclarations.Validate`, called from the
   `LlmRequest` constructor, `LlmResponseNormalizer`, and
   `ContentToolCallExtractor` with ordinal comparison), with the same rule
   for native and pseudo-tool paths. The error names the duplicate without
   tool arguments; declaration order never selects a winner.
2. The replaceable authoritative argument-validator boundary
   (`ILlmToolArgumentValidator`) runs after Nuwa repair in both the
   normalizer and the extractor. The structural default reports typed,
   path-aware failures and explicit unsupported keywords without mutating
   arguments; failures mark calls `InvalidArguments` with validator
   diagnostics. `AddLlmTools` registers the default; adapters replace it.
   Repair acceptance, schema validation, CLR mapping, and application
   validation remain distinct stages.

Marang should not build a competing validator or silently choose a duplicate
schema. A temporary Marang adapter may inject an authoritative validator only
until the Baize boundary is released.

### P1 — make safety bounds explicit for buffered model output

`LlmStreamingExtensions.CompleteAsync`, structured-output repair, pseudo-tool
extraction, and tool-argument assembly use in-memory buffers. `MaxTokens` is a
provider request hint, not a Baize-enforced character/byte bound, and the
current common request has no bound for messages, tool schemas, metadata, or
repair input. Add opt-in, documented limits and deterministic overflow
outcomes for response/tool-argument/repair buffering, with tests that prove
bounded memory behavior and cancellation. Until then, a Marang adapter must
set provider output limits and count/stop streamed content before handing it to
repair or evidence; it must not assume Baize bounds untrusted model output.

### P1 — preserve provenance without payload leakage

Baize exposes useful provider/model/endpoint, response-ID, usage, rate-limit,
finish, router, and repair diagnostics, but no common immutable invocation
identity, capability/profile snapshot, tool-schema identity, attempt/retry
identity, or input-artifact references. Add a bounded, payload-free provenance
projection only if it remains useful to multiple Baize consumers; it must not
turn request metadata or provider metadata into an unbounded transcript store.
Marang can proceed with an adapter-owned receipt that supplies these missing
identities and whitelists Baize fields.

## Marang adapter responsibilities (do not move into Baize)

- Implement the durable provider seam (`Start` with an idempotency identity,
  `Observe`, `GetResult`, idempotent `Cancel`, and explicit `Resume`) around a
  provider that actually exposes a durable operation handle. A one-shot
  `ILlmClient.CompleteAsync` wrapped in a task is not recoverable after a lost
  acknowledgement and must not be retried as if it were.
- Map Baize completion/tool results into Marang's `ExecutionAttempt`,
  `NodeGeneration`, artifact, and evidence identities. Preserve usage and
  provider diagnostics, but redact/whitelist metadata and never persist full
  prompts, credentials, signed URLs, or worker transcripts by default.
- Enforce Marang's semantic profile, budget, context-size, tool allow-list,
  workspace authorization, provider selection, and response-size policy.
  Baize capability declarations are compatibility filters, not tenant or
  side-effect authorization.
- Own tool execution, approval, sandboxing, retries, compensation, and
  deterministic validation. A normalized tool call is data, not permission to
  invoke a tool; model claims never override deterministic test receipts.
- Distinguish local cancellation, timeout, transport failure, provider
  rejection, malformed/invalid output, and accepted work that is still
  running. On ambiguity, re-observe a known handle or return an unknown outcome;
  never issue a duplicate billable start merely because the response was lost.
- Keep direct completion integration separate from `IGenerationClient` and
  `IBaizeBatchClient` unless the selected provider's operation semantics match
  the Marang activity. Those APIs are useful precedents for handles and
  status/result mapping, not a license to make artifact or batch types part of
  Marang core contracts.

## Gate decision

Baize is suitable now for Marang's in-memory bounded model provider and for
provider-specific batch/generation adapters, provided the adapter owns policy,
provenance completion, and output bounds. Marang must wait for the two P0 tool
integrity items before accepting complex model tool contracts as authoritative.
Durable ordinary model execution remains conditional on a provider-native
handle-based adapter; no Baize roadmap item should add durable workflow state
or Marang lifecycle semantics to the core client.

## Evidence inspected

- `src/Penghou.Baize/ILlmClient.cs`, `ILlmCompletionClient.cs`, `LlmRequest.cs`,
  `LlmResponse.cs`, `LlmUsage.cs`, `LlmProviderDiagnostics.cs`,
  `LlmClientMetadata.cs`, `LlmClientFailureKind.cs`, and `LlmRequestValidator.cs`.
- `src/Penghou.Baize/IBaizeBatchClient.cs`, `ProviderBatchHandle.cs`, and
  `src/Penghou.Baize/Generation/IGenerationClient.cs`,
  `GenerationOperationHandle.cs`, `GenerationExecutorCore.cs`.
- `src/Penghou.Baize.Tools/LlmResponseNormalizer.cs`,
  `LlmStructuredOutputRepairer.cs`, `StructuredOutputRepairingLlmClientDecorator.cs`,
  `ContentToolCallExtractor.cs`, and `LlmToolResultParserBase.cs`.
- `tests/Penghou.Baize.Tests/*Generation*`, `LlmClientBaseTests.cs`,
  `CanonicalImmutabilityTests.cs`, and the structured-output/tool repair tests
  under `tests/Penghou.Baize.Tools.Tests`.
- `docs/scope-and-boundaries.md`, `docs/roadmap-generation-client.md`, and
  `docs/roadmap-tool-integrity.md`.
