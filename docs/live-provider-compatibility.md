# Live provider compatibility

This page records behavior verified against real provider APIs. It complements
deterministic unit tests; it is not a claim that every model exposed through a
provider shares the same wire behavior.

## Current compatibility matrix

`Pass` means the tagged live contract completed successfully for the exact
provider, model, API style, and date shown. `Not tested` means no claim is made.

| Provider | Model | API | Baseline streaming | Native tools | Multi-turn tools | Parallel tools | Structured output | Image input | Audio input | Video input | PDF/file input | Image generation | Explicit thinking | Native batch | Last verified |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Gemini | `gemini-3.6-flash` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Blocked: GenerationClient required | Pass | Pass | 2026-08-12 |
| Gemini | `gemini-3.5-flash-lite` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Not tested | Pass | Pass | 2026-08-12 |
| Gemini | `gemini-3.5-flash` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Not tested | Pass | Pass | 2026-08-12 |
| Gemini | `gemini-3.1-flash-lite` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Not tested | Pass | Pass | 2026-08-12 |
| Gemini | `gemini-2.5-flash-lite` | Native `v1beta` | Blocked: unavailable to new users | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-2.5-flash` | Native `v1beta` | Blocked: unavailable to new users | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-2.5-pro` | Native `v1beta` | Blocked: unavailable to new users | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-3.1-pro-preview` | Native `v1beta` | Pass | Unstable: malformed call | Pass | Unstable: malformed call | Pass | Pass | Pass | Pass | Pass | Blocked: GenerationClient required | Pass | Pass | 2026-08-12 |
| Gemini | `gemini-3.1-flash-lite-image` | Interactions `v1beta` | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Provider probe: Pass | Not tested | Not applicable | 2026-08-12 |
| DeepSeek | `deepseek-v4-flash` | OpenAI-compatible `/v1` with `DeepSeek` dialect | Pass | Pass | Pass | Pass | Pass: tool-backed | Not tested | Not tested | Not tested | Not tested | Not tested | Pass | Not tested | 2026-08-12 |
| DeepSeek | `deepseek-v4-pro` | OpenAI-compatible `/v1` with `DeepSeek` dialect | Pass | Pass | Pass | Pass | Pass: tool-backed | Not tested | Not tested | Not tested | Not tested | Not tested | Pass | Not tested | 2026-08-12 |
| DeepSeek | `deepseek-v4-flash` | Claude-compatible `/anthropic` | Pass | Pass | Pass | Pass | Pass: tool-backed | Not tested | Not tested | Not tested | Not tested | Not tested | Pass | Not tested | 2026-08-12 |
| DeepSeek | `deepseek-v4-pro` | Claude-compatible `/anthropic` | Pass | Pass | Pass | Pass | Pass: tool-backed | Not tested | Not tested | Not tested | Not tested | Not tested | Pass | Not tested | 2026-08-12 |

## Gemini native API observations

- Gemini's native schema dialect rejected the canonical JSON Schema keyword
  `additionalProperties`. The Gemini adapter now removes that keyword only
  from the wire schema while retaining the canonical schema for strict local
  validation. Native tools and structured output both passed after adaptation.
- The sequential tool contract passed on all five available tested models. Each selected an
  inventory lookup from three candidates, consumed the local result, calculated
  a restock amount of eight, called the restock tool, consumed its result, and
  returned the exact plan ID. Raw assistant parts were replayed between turns,
  exercising Gemini continuation signatures as well as ordinary tool results.
- The parallel tool contract passed on 3.6 Flash, 3.5 Flash-Lite, and 3.1
  Flash-Lite. Each returned the
  weather and exchange-rate calls together in one response, accepted both tool
  results in one message, and produced the exact combined answer. Replaying a
  two-call assistant turn also exercised Baize's parallel-capability inference
  and endpoint validation on the follow-up request.
- The first parallel response from 3.5 Flash contained both required calls,
  but its follow-up first hit the five-request-per-minute free-tier quota and a
  later isolated retry returned HTTP 503 after 78 seconds due to high demand.
  The complete contract therefore remains blocked rather than failed.
- Both 2.5 Flash variants returned HTTP 404 stating that the model is no longer
  available to new users. Paid access did not change this result, and 2.5 Pro
  returned the same 404. The Models API still lists all three because they are
  available through the newer Interactions API; discovery therefore does not
  imply that the legacy `generateContent` endpoint accepted by Baize can invoke
  them for a newly created project.
- Gemini 3.1 Pro Preview passed baseline, structured output, explicit thinking,
  the sequential two-stage tool workflow, all four multimodal input contracts,
  and native batch. Its simple and parallel tool contracts each failed twice:
  the provider returned `MALFORMED_FUNCTION_CALL` with an empty function call.
  This is recorded as unstable preview-model generation rather than missing
  tool support because the more complex sequential tool flow passed.
- Thinking tokens share `maxOutputTokens` with visible text and function calls.
  A 128-token tool test spent 118 tokens thinking and ended with `MAX_TOKENS`
  before emitting a call. A 512-token budget completed successfully.
- `thinkingBudget: 0` was rejected by `gemini-3.6-flash` with HTTP 400. The
  Gemini adapter can encode that wire-level control, but an endpoint profile
  for this model must narrow `ThinkingDisable` to `false`. The baseline test
  uses provider-default thinking.
- Live responses reported `modelVersion`, `responseId`, `serviceTier`, and
  `thoughtsTokenCount`; the Gemini client maps these into Baize usage and
  provider diagnostics.
- Native batch passed end to end on all five currently available tested
  models after enabling paid access. Current v1beta operations expose
  `response.responsesFile` directly and result files download through
  `/download/v1beta/files/{id}:download`; both differed from the older shapes
  represented by the original adapter tests. Baize now accepts both result
  envelopes and uses the current download endpoint. Even a single-item batch
  took roughly two to four minutes, so this API is appropriate for durable
  asynchronous work rather than interactive latency.
- Explicit low-effort thinking passed on all five available tested models. An
  initial 3.6 Flash request returned no headers before .NET's default
  100-second `HttpClient` timeout, while the isolated retry completed in two
  seconds. The live harness now uses a configurable five-minute HTTP timeout
  so transient slow reasoning is not misclassified as incompatibility.
- Inline PNG input passed using the deterministic 128 by 128 solid-red fixture
  at `tests/Penghou.Baize.IntegrationTests/Assets/solid-red.png.base64`.
  Gemini returned the exact expected dominant color. The base64 wrapper keeps
  the binary PNG portable and reviewable while the test sends decoded bytes.
- Inline WAV input passed on all five available tested models. The test constructs a small,
  deterministic mono PCM16 recording containing three separated tones and
  requires the model to distinguish audible tones from silence. A preliminary
  exact-count assertion produced inconsistent counts across model generations
  and was correctly removed as a model-quality benchmark rather than a client
  compatibility contract.
- Inline AVI input passed on all five available tested models. The fixture is constructed in
  memory without external media tools and contains six seconds of solid red,
  green, and blue sections. Requiring the exact chronological sequence checks
  temporal visual understanding rather than merely accepting video bytes.
- Inline PDF input passed on all five available tested models through `LlmFileContent`. The
  dependency-free fixture contains a reference code and two quantities; the
  exact response requires both extraction and a small calculation, confirming
  that the attachment content reached the model as a document.
- Paid image generation passed a provider-level contract probe using
  `gemini-3.1-flash-lite-image` through `POST /v1beta/interactions`. The response
  contained decodable image bytes and an image MIME type. The probe deliberately
  bypasses `ILlmClient`: its response types cannot represent generated binary
  artifacts, and silently discarding the image would produce a false result.
  The provider-neutral surface remains the GenerationClient described in the
  generation roadmap.

## DeepSeek API-style observations

- Both V4 Flash and V4 Pro passed the same baseline streaming, single native
  tool, sequential multi-turn tool, parallel tool, and explicit-thinking
  contracts through the OpenAI-compatible and Claude-compatible APIs. This
  confirms the tested behavior is not tied to only one Baize adapter.
- The OpenAI-compatible endpoint is `https://api.deepseek.com/v1` and should
  use the Baize `DeepSeek` dialect so thinking controls and reasoning content
  use DeepSeek's wire shape. The Claude-compatible endpoint base is
  `https://api.deepseek.com/anthropic`; using the ordinary DeepSeek base with
  the Claude client produces a 404 at `/v1/messages`.
- DeepSeek returned two independent tool calls in one response for both models
  and both API styles. OpenAI provider defaults remain conservative, while the
  explicit `DeepSeek` dialect now advertises the verified parallel-tool
  behavior so replayed parallel turns pass routing validation.
- OpenAI-compatible native JSON Schema output is not available for either
  tested model. Both returned HTTP 400 with `This response_format type is
  unavailable now` when sent `response_format.type = json_schema`. Baize now
  uses capability-driven synthetic-tool output for DeepSeek's OpenAI dialect,
  matching the Claude-compatible recovery. The complete schema contract passed
  through this fallback for both V4 Flash and V4 Pro.
- DeepSeek rejected a forced synthetic `tool_choice` while provider-default
  thinking remained active. The adapter now disables thinking only for this
  tool-backed schema request; ordinary and explicitly requested thinking calls
  retain their existing behavior.
- DeepSeek's documented OpenAI-compatible response mode is `json_object`, not
  `json_schema`. Minimal live probes returned valid JSON with the expected
  shape for both V4 Flash and V4 Pro. This mode guarantees syntactically valid
  JSON, but callers must still describe the desired shape in the prompt and
  validate it locally; it is not server-enforced JSON Schema output.
- Ordinary tool definitions accept JSON Schema through OpenAI
  `function.parameters` and Claude `input_schema`; the single, sequential, and
  parallel tool contracts all exercised schema-shaped arguments successfully.
  This should not be confused with DeepSeek's stricter server-enforced mode.
- DeepSeek strict tool mode is a separate beta OpenAI-compatible feature. It
  requires the `https://api.deepseek.com/beta` base and `strict: true` on every
  function. Minimal live probes for both models returned one tool call matching
  nested string-pattern, bounded-integer, enum, required-property, and
  `additionalProperties: false` constraints.
- The current beta validator did not exactly match the published schema subset:
  it accepted `minLength` and a schema missing `additionalProperties: false`,
  but rejected an optional declared property and rejected `oneOf`. Baize should
  therefore expose strict mode explicitly and preserve the canonical schema;
  it should not silently rewrite schemas based on a potentially stale subset.
- Explicit thinking passed through both API styles and surfaced reasoning text.
  The Claude-compatible response supplied usage but no provider-specific
  diagnostics object; router diagnostics and HTTP diagnostics remained
  available. Live contracts therefore treat provider diagnostics as optional.
- Multimodal input, image generation, and native batch were not exercised. No
  compatibility claim is made for those surfaces.

See the provider-specific [DeepSeek setup and behavior guide](providers/deepseek.md).

## Verification history

The chronological evidence is maintained in the
[live provider verification log](live-provider-verification-log.md). This page
keeps the current compatibility claims and provider observations concise.

## Running a capability

Each paid live test has both `Category=Live` and one `Capability` trait. Run a
single surface without invoking already-verified model features:

```powershell
dotnet test Penghou.Baize.IntegrationTests.slnx `
  --configuration Release `
  --filter "Category=Live&Capability=StructuredOutput"
```

Current capability names are:

- `Baseline`
- `Tools`
- `ComplexTools`
- `ParallelTools`
- `StructuredOutput`
- `ImageInput`
- `AudioInput`
- `VideoInput`
- `FileInput`
- `ImageGeneration`
- `Thinking`
- `Batch`

Omit `--filter` to run the complete live suite. Unselected tests make no
provider requests and consume no model tokens.
