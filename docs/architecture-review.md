# Architecture & quality review — findings

Reviewed: 2026-08 against release 0.3.0-preview.4 line.
**Ledger updated after remediation:** all actionable review findings are
resolved. This document is now a completion record rather than an open backlog.

Scope: all 12 src projects — core chat stack (`ILlmClient`), Generation,
Batch, Tools, Router, Diagnostics, Extensions.AI bridge, and the OpenAI /
Claude / Gemini / Ollama / Runway / fal providers.

## Resolved since review (do not re-track)

**P0 — billable impact (all 7):**
fal payload builders map every validated field (aspect/size/format/duration/
audio/last-frame/references); Runway fails fast on video-to-video,
last-frame, references, explicit sizes, and idempotency keys instead of
silently degrading; batch submit/poll isolation converts unexpected
exceptions into per-chunk failures so submitted handles survive;
`ReadJsonAsync` clones/disposes (no leaked `JsonDocument`);
`IdempotencyKey` implemented (fal header, fail-fast elsewhere, per-chunk
derived keys in batches); `BaizeImageGenerator` polls queued operations with
resumable-handle timeouts; coordinator status/results/cancel preserve
partial outcomes.

**P1 — architecture (all 7):**
Status-code classification unified across taxonomies (401=Authentication,
403=Authorization) with a parity test guarding drift; `BaizeException` moved
to the root namespace; shared `GenerationExecutorCore` (selection, polling
loop, failure factories) serves both executors; non-throwing
`GenerationRequestValidator.TryValidate` replaced catch-as-boolean probing;
`WaitAsync(handle)` resume APIs on both executor interfaces; core owns the
`llm` named transport via `AddBaizeTransport` (100s default timeout);
endpoint descriptors give eager registration + startup option validation.

**P2 — duplication & consistency (all 14):**
`LlmJson.ParseElement`; `BaizeBatchClientBase` under Claude/Gemini/OpenAI
batch clients; `EnsureHandleOwnership` in `GenerationClientBase`;
auth template method across the chat family with omit-empty credentials;
ctor guards everywhere; `GeminiUrl.LooksLikeApiVersion` +
`LlmThinking.MapStandardEffort`; `GeminiResponseMapping` shared by chat and
batch; generic `AddBaizeGenerationEndpoint<TOptions>` DI helper;
`BaizeHttp.ClientName`; OpenAI diagnostics parity (id/model/
system_fingerprint/service_tier surfaced, per-chunk diagnostic events);
honest XML docs; `LlmClientBase` state as properties.

**P3 — robustness closes:**
Caller cancellation excluded from failure metrics; fal multi-input mapped to
references; Runway unparseable outputs fail loudly; deterministic Gemini
tool-call ids (`call_{n}`); mid-poll unexpected exceptions wrapped as typed
batch-chunk failures. OpenAI first-choice reads documented as contractual
(no `n` parameter is ever sent). Error bodies are bounded, signed asset URLs
are sanitized, OpenAI tool-call diagnostics include the current chunk and an
authoritative `[DONE]` snapshot, robustness paths have direct regression tests,
corrupted XML punctuation has been repaired, and Ollama's separate `thinking`
field is preserved as ordered canonical reasoning content.

## Final remediation closes

- Generation can consume Router reliability ordering through
  `IGenerationEndpointOrderer`; the default bridge uses the same cooldown and
  failure memory as chat routing while preserving submit-at-most-once safety.
- fal-issued status/result/cancel URLs travel with persisted operation handles
  and are used verbatim after validating their HTTP(S) scheme.
- Multi-target schema tests enforce a canonical enum shape; the .NET 8 fallback
  and .NET 9+ exporter are normalized consistently.
- The async policy is explicit in `docs/async-policy.md`; diagnostics dispose
  paths no longer block on asynchronous writes.
- Runway uploads use `ReadOnlyMemoryContent`; OpenAI batch normalization checks
  its successful-response invariant explicitly.
- Runway offers cohesive options-based direct construction, well-known setting
  and protocol names are public constants, and typed generation progress
  surfaces phase and queue position without removing the numeric compatibility
  projection.
- Structured-output repair has an explicit native-streaming opt-out. The M.E.AI
  bridge preserves malformed tool arguments as `$raw` and supports explicit
  ownership/disposal of wrapped clients; unknown content remains a deliberate,
  early validation error rather than being silently discarded.
- Public API analyzers and baselines cover every shipped package. A BenchmarkDotNet
  project covers stream assembly, schema generation, and registry scaling.
- Raw strings remain at provider wire/configuration boundaries intentionally:
  API keys integrate naturally with configuration and secret providers, while
  aspect-ratio and format vocabularies vary by model. Validation and capability
  descriptors provide the type-safe boundary without freezing provider vocabularies.

## Done well (preserve)

1. Billing-safe "submit at most once": ambiguous submissions classify as
   `UnknownSubmissionOutcome` and are never auto-retried, consistently across
   transport and executors.
2. Router failover commit-buffering: pre-content events buffered until an
   endpoint commits, so failover cannot mix providers' output.
3. Per-provider truncation detection raising Availability-classified errors so
   routers fail over rather than return partial answers.
4. Spec-faithful SSE framing including WHATWG event-type-buffer reset; rate
   limit parsing covering both OpenAI and Anthropic conventions.
5. Diagnostics redaction of sensitive headers/params with bounded captures.
6. Batch polling infrastructure: TimeProvider/jitter injection, RetryAfter
   honored, clamped backoff with transient-failure budget.
7. Modern hygiene: nullable + TreatWarningsAsErrors + deterministic builds,
   immutable sealed records, discriminated unions for media/asset sources.

## Remaining roadmap

No architecture-review findings remain open. Future feature work belongs in
the product roadmap and should be added only when backed by a concrete use case.
