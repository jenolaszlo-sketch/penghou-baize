# Architecture & quality review — findings

Reviewed: 2026-08, branch `features/p3-minimal-vertical` @ release
0.3.0-preview.2 line. Read-only audit; no code changes accompany this document.

Scope: all 12 src projects — core chat stack (`ILlmClient`), Generation,
Batch, Tools, Router, Diagnostics, Extensions.AI bridge, and the OpenAI /
Claude / Gemini / Ollama / Runway / fal providers. 13 test projects (569 cases
including live-provider integration).

## Summary

The solution's distributed-systems instincts are excellent — billing-safe
submission, failover commit-buffering, per-provider truncation detection are all
real and tested. The dominant theme of the findings is **duplication between two
parallel stacks** (chat vs generation) that each grew their own routing,
validation, and error taxonomy, plus a set of **billable-impact correctness
gaps** in the media providers where validated request fields are silently
dropped.

## Resolved since review

All seven P0 findings below were fixed and regression-tested
(`Fix P0 generation issues, raise core coverage above threshold` and
`Implement idempotent submission and queued image generation`):

1. fal payload builders now map every validated field (`aspect_ratio`,
   `image_size`, `video_size`, `output_format`, `duration`,
   `generate_audio`, `last_image_url`, `reference_image_urls`) with MIME
   formats normalized to the bare form.
2. Runway fails fast with `UnsupportedCapability` for `SourceVideo`,
   `LastFrame`, `References`, and explicit pixel sizes instead of silently
   degrading video-to-video to text/image-to-video.
3. The batch executor's submit and poll sweeps convert unexpected exceptions
   into per-chunk failures, so already-submitted billable handles are always
   reported; caller cancellation still propagates.
4. `GenerationClientBase.ReadJsonAsync` clones the parsed root and disposes
   the document — no undisposed `JsonDocument` escapes to callers.
5. `GenerationRequest.IdempotencyKey` is implemented: fal forwards it as
   `x-fal-idempotency-key`; Runway and OpenAI generation fail fast when a key
   is set (no provider-side mechanism exists); batch chunks derive
   deterministic per-chunk keys (`{key}-{index}`).
6. The M.E.AI `BaizeImageGenerator` polls queued operations to a terminal
   state (`BaizeImageGeneratorOptions.PollInterval`/`Timeout`) and every
   timeout/no-retrieval error carries the resumable operation handle.
7. Coordinator status/results/cancel no longer lose partial outcomes: status
   surfaces per-part failures as Failed entries (transients still retry),
   and results/cancel attempt every part before aggregating failures.

## P1 — Architecture & boundaries

8. **Two parallel stacks with divergent maturity** — generation re-implements a
   weaker registry/routing/validation than Router (first-candidate policy vs
   reliability-ranked selection + cooldown memory + `ISecretProvider` + config
   reload). Extract shared primitives so generation inherits them.
9. **Four overlapping error taxonomies** — `LlmClientFailureKind`,
   `GenerationErrorKind`, router's `LlmRoutingFailureKind`/
   `LlmConfigurationFailureKind`, and two exception hierarchies whose status
   classification **disagrees**: `BaizeException.ClassifyStatusCode` maps
   403→Authorization; `LlmClientException` maps 401/403→Authentication.
   `BaizeException` also lives in namespace `.Generation` while the root is
   `.Baize`.
10. **~110 lines verbatim duplicated between the two executors**
    (`GenerationExecutor.cs` vs `GenerationBatchExecutor.cs`:
    SelectEndpoint/Supports/CreateFailure/CreateTimeout/etc.) — also the single
    place to add wait-by-handle resume support (#12).
11. **Capability filtering via caught exceptions** — `Supports()` runs full
    validation per candidate and uses `catch` as a boolean. A non-throwing
    `TryValidate(capabilities, request, out diagnostics)` would be cheaper and
    answers the structured-diagnostics gap (both validators are throw-only).
12. **Timeout tells callers to resume "with this handle" but exposes nothing to
    resume with** — handle id exists only in message text; neither executor
    interface has a wait-by-handle API.
13. **Layering inversion: optional Diagnostics package owns core transport** —
    the `"llm"` named HttpClient is registered only in
    `Diagnostics\ServiceCollectionExtensions.cs` but consumed via magic string
    in ~10 sites across core/providers/generation; no default timeout anywhere.
14. **Provider registration via side-effecting lazy DI factories** —
    `registry.Register(...)` runs only when the keyed service resolves;
    unresolved endpoints are invisible to routing (late failure), and options
    validation at startup is absent (`ApiKey = ""` allowed until first call).

## P2 — Duplication & consistency

15. **`ParseJsonElement` copy-pasted four times** — LlmClientBase plus the
    OpenAI/Claude/Gemini request mappers, byte-for-byte identical.
16. **Three batch clients share ~70% structure with no base class** — identical
    send/classify loops, JSONL splitting, duplicated JsonOptions snapshots per
    provider (chat + batch pairs too).
17. **`EnsureHandleOwnership` duplicated verbatim ×4** across generation
    clients — belongs in `GenerationClientBase`.
18. **Auth template-method violation in the chat family** — headers hand-rolled
    inside each `CreateHttpRequest`, while generation got `ApplyAuth` right.
19. **Inconsistent credential edge handling** — OpenAI/Ollama omit empty keys;
    Claude/Gemini always send.
20. **Constructor guards inconsistent** — Gemini/Ollama validate inputs;
    OpenAI/Claude validate nothing (null baseUrl → later NRE); fal mixes guard
    styles.
21. **Double/split-brain validation** — OpenAI validates twice (base validator +
    mapper-internal); Ollama defers media rules to mapping time.
22. **Triplicated helpers with divergent implementations** —
    `LooksLikeApiVersion` ×3 (two different digit checks);
    `MapThinkingEffort` ×2; effort-budget tables differing only in constants.
23. **Gemini response mapping duplicated chat/batch** (finish reasons, token
    sums, thought-signature continuations).
24. **DI boilerplate repeated ×5** for generation endpoints — one generic
    helper would do; note the lazy-registration side effect above.
25. **`"llm"` named-client magic string ~10 sites**; shared constant needed.
26. **Diagnostics parity gaps** — Gemini surfaces actual model/service tier/
    thinking tokens; Ollama reports timings and tokens/sec; OpenAI wire models
    drop `id`/`model`/`system_fingerprint`/`service_tier` entirely.
27. **Misleading `<inheritdoc />` on private methods** with no base member.
28. **Base-class state as protected fields** (`LlmClientBase`) vs property-based
    encapsulation (`GenerationClientBase`) — consistency nit.

## P3 — Robustness edges

29. **Cancellation inflates failure metrics** — `LlmClientBase.StreamAsync`
    counts cancelled streams as failures (catch-all telemetry).
30. **Silent data loss in mappings** — fal uses only `Inputs[0]`; Runway skips
    unparseable output URLs; OpenAI streaming reads only `Choices[0]`;
    reasoning content dropped on non-DeepSeek dialects and entirely by Ollama.
31. **Hidden randomness breaks testability/correlation** — Gemini synthesizes
    tool-call ids via `Guid.NewGuid()` mid-parse (Ollama correctly leaves null).
32. **fal deserializes `response_url`/`status_url`/`cancel_url` then rebuilds
    URIs manually** — use provider-supplied URLs.
33. **Schema generator diverges between TFMs** — NET9 JsonSchemaExporter path
    vs NET8 reflection fallback emitting enums as integers; no golden-schema
    parity test.
34. **Non-BaizeException mapping of provider job failures** mid-poll in
    Runway/fal `GetAsync` — inconsistent with modeling job failure as operation
    error state.

## P4 — Async, resources, polish

35. **`ConfigureAwait(false)` inconsistent solution-wide** — present in
    Generation executors/streaming extensions; absent from both client bases,
    Router, Batch, Tools, Extensions.AI. Pick one policy; enforce via analyzer.
36. **Sync-over-async residue** in diagnostics capture dispose paths.
37. **Error messages embed full payloads** (`ParseJsonElement(...): {json}`,
    batch send failures) — log-bloat/PII risk; truncate.
38. **`RunwayUploadFileAsync` copies memory needlessly** —
    `ReadOnlyMemoryContent` avoids the copy.
39. **Fragile non-null invariant in `OpenAiBatchClient.NormalizeResult`** — one
    edit away from `InvalidOperationException`.
40. **Primitive obsession** — API keys as raw strings end-to-end; aspect
    ratios/formats as free strings; `LlmProviderKey` shows the right pattern.

## Usability

41. **Constructor explosion with positional string traps** —
    `RunwayGenerationClient` takes 10 params including consecutive nullable
    strings; options objects exist at DI layer but not for direct construction;
    no `HttpClient`-direct overload for tests.
42. **Magic settings keys discovered only by reading code** —
    `"structured_output"` (duplicated), `"Dialect"`, `"ThinkingStyle"` parsed
    late via `Enum.TryParse` with throw-at-resolution.
43. **Typed progress missing** — fal queue position buried in
    `ProviderMetadata`; a typed `GenerationProgress` (state/phase/position)
    would serve UIs better than bare `IProgress<double>`.
44. **Repair decorator silently destroys streaming** for schema requests (full
    buffering, no opt-out knob).
45. **M.E.AI adapter brittleness** — unknown `AIContent` types throw
    mid-request; tool-call argument parse errors propagate unguarded; adapters
    never dispose disposable wrapped clients.

## Release engineering

46. **No PublicApi baselines** — Zhinu adopted `PublicApiAnalyzers` +
    `PublicAPI.*.txt` baselines; Baize (larger surface, preview) has none.
47. **No benchmarks project** — token throughput, SSE parse rate, router
    failover latency, batch scaling unmeasured (Zhinu pattern portable).
48. **CI**: verify all three TFMs build/test/pack and consider package
    validation + format verify parity with Zhinu.

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

1. ~~**P0 billing fixes** (#1–#7)~~ **Done** — see "Resolved since review".
2. **Unify the error taxonomy** and status-code classification; typed
   timeout-with-handle resume API.
3. **Extract shared routing/registry/validation primitives**; eager endpoint
   registration + startup validation; move `"llm"` client registration into core.
4. **De-duplicate**: ParseJsonElement, batch base class, EnsureHandleOwnership,
   auth template method, Gemini mappers; centralize JSON options.
5. **Adopt PublicApi baselines + benchmarks** (port tooling from Zhinu).
6. Ergonomics batch: constructor options objects, typed progress, streaming
   opt-out knob, M.E.AI adapter hardening (unknown-content handling,
   disposable wrapping).
