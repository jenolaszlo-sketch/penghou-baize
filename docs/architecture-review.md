# Architecture & quality review — findings

Reviewed: 2026-08 against release 0.3.0-preview.2 line.
**Ledger updated after remediation:** P0, P1, and P2 are fully resolved; P3 is
nearly complete. This document now tracks only what is still open — resolved
work is summarized once below and no longer itemized.

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

**P3 — robustness (5 of 6):**
Caller cancellation excluded from failure metrics; fal multi-input mapped to
references; Runway unparseable outputs fail loudly; deterministic Gemini
tool-call ids (`call_{n}`); mid-poll unexpected exceptions wrapped as typed
batch-chunk failures. OpenAI first-choice reads documented as contractual
(no `n` parameter is ever sent).

## Open — carry-over from P1

### 1. Generation does not yet inherit Router reliability primitives

Shared executor/descriptor plumbing landed, but generation routing still uses
first-candidate selection rather than the Router's reliability-ranked
selection, cooldown memory, and `ISecretProvider`. Extract or bridge those
primitives so both stacks share them.

## Open — P3 remainder

### 2. Ollama reasoning content not surfaced

Ollama models can emit thinking output; the client neither streams nor
projects it into `ReasoningContent`, unlike every other provider.

### 3. fal rebuilds provider-supplied URIs manually

`response_url`/`status_url`/`cancel_url` are deserialized then reconstructed
from a base prefix. Use the URLs the provider returns.

### 4. Schema generator TFM parity test

net9/net10 use the JSON Schema exporter path; net8 falls back to reflection
that can emit enums differently. Needs a golden-schema parity harness across
target frameworks.

## Open — P4 polish

### 5. ConfigureAwait policy inconsistent solution-wide

Present in generation executors/streaming extensions; absent in client bases,
Router, Batch, Tools, Extensions.AI. Pick one policy and enforce via analyzer.

### 6. Sync-over-async residue in diagnostics capture dispose paths.

### 7. Error messages embed full payloads

`LlmJson.ParseElement` and batch send failures echo entire bodies — truncate
before logging/throwing (log-bloat/PII risk).

### 8. `RunwayUploadFileAsync` copies memory needlessly — use `ReadOnlyMemoryContent`.

### 9. Fragile non-null invariant in `OpenAiBatchClient.NormalizeResult`.

### 10. Primitive obsession — raw string API keys end-to-end; free-string
aspect ratios/formats (`LlmProviderKey` shows the right pattern).

## Open — usability

### 11. Constructor explosion in direct construction

`RunwayGenerationClient` takes 10 positional parameters including
consecutive nullable strings; an options object exists at the DI layer but
not for direct construction/tests.

### 12. Magic settings keys discovered only by reading code

`"structured_output"` (duplicated), `"Dialect"`, `"ThinkingStyle"` parsed late
via `Enum.TryParse` with throw-at-resolution.

### 13. Typed progress missing — fal queue position buried in
`ProviderMetadata`; a typed `GenerationProgress` (state/phase/position) would
serve UIs better than bare `IProgress<double>`.

### 14. Repair decorator silently destroys streaming for schema requests
(full buffering, no opt-out knob).

### 15. M.E.AI adapter brittleness — unknown `AIContent` types throw
mid-request; tool-call argument parse errors propagate unguarded; adapters
never dispose disposable wrapped clients.

## Open — release engineering

### 16. No PublicApi baselines — adopt Zhinu's `PublicApiAnalyzers` +
`PublicAPI.*.txt` pattern.

### 17. No benchmarks project — token throughput, SSE parse rate, router
failover latency, batch scaling (Zhinu pattern portable).

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

## Suggested priority

1. **Small robustness closes**: Ollama reasoning surfacing (#2), fal
   provider-supplied URLs (#3), payload truncation in errors (#7).
2. **Release engineering**: PublicApi baselines + benchmarks (#16, #17) —
   port tooling from Zhinu.
3. **Usability batch**: Runway options object (#11), named settings keys
   (#12), M.E.AI adapter hardening (#15), repair streaming opt-out (#14).
4. **Deeper convergence**: generation inheriting Router reliability
   primitives (#1); ConfigureAwait analyzer decision (#5).
5. **Nice-to-have**: typed progress (#13), primitive-obsession types (#10),
   schema parity harness (#4), sync-over-async residue (#6),
   `NormalizeResult` invariant (#9), upload copy (#8).
