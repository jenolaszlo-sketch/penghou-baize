# Live provider compatibility

This page records behavior verified against real provider APIs. It complements
deterministic unit tests; it is not a claim that every model exposed through a
provider shares the same wire behavior.

## Current compatibility matrix

`Pass` means the tagged live contract completed successfully for the exact
provider, model, API style, and date shown. `Not tested` means no claim is made.

| Provider | Model | API | Baseline streaming | Native tools | Multi-turn tools | Parallel tools | Structured output | Image input | Audio input | Video input | PDF/file input | Image generation | Explicit thinking | Native batch | Last verified |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Gemini | `gemini-3.6-flash` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Blocked: GenerationClient and paid tier required | Not tested | Blocked: paid tier required | 2026-08-12 |
| Gemini | `gemini-3.5-flash-lite` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Not tested | Pass | Not tested | 2026-08-12 |
| Gemini | `gemini-3.5-flash` | Native `v1beta` | Pass | Pass | Pass | Blocked: quota/high demand | Pass | Pass | Pass | Pass | Pass | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-3.1-flash-lite` | Native `v1beta` | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Pass | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-2.5-flash-lite` | Native `v1beta` | Blocked: unavailable to new users | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | 2026-08-12 |
| Gemini | `gemini-2.5-flash` | Native `v1beta` | Blocked: unavailable to new users | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | Not tested | 2026-08-12 |

## Gemini native API observations

- Gemini's native schema dialect rejected the canonical JSON Schema keyword
  `additionalProperties`. The Gemini adapter now removes that keyword only
  from the wire schema while retaining the canonical schema for strict local
  validation. Native tools and structured output both passed after adaptation.
- The sequential tool contract passed on all four available tested models. Each selected an
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
  available to new users. Their advertised model entries do not imply that a
  newly created API project can invoke them.
- Thinking tokens share `maxOutputTokens` with visible text and function calls.
  A 128-token tool test spent 118 tokens thinking and ended with `MAX_TOKENS`
  before emitting a call. A 512-token budget completed successfully.
- `thinkingBudget: 0` was rejected by `gemini-3.6-flash` with HTTP 400. Baize
  therefore does not claim a universal Gemini thinking off-switch. The
  baseline test uses provider-default thinking.
- Live responses reported `modelVersion`, `responseId`, `serviceTier`, and
  `thoughtsTokenCount`; the Gemini client maps these into Baize usage and
  provider diagnostics.
- Native batch upload and finalization succeeded and produced an `ACTIVE`
  JSONL file, but batch creation returned `FAILED_PRECONDITION` on the free
  tier. The 3.6 Flash pricing table lists Batch as unavailable on the free tier.
  The current 3.5 Flash-Lite table advertises free Batch access, so that model
  remains a separate candidate for a paced live batch test.
- Inline PNG input passed using the deterministic 128 by 128 solid-red fixture
  at `tests/Penghou.Baize.IntegrationTests/Assets/solid-red.png.base64`.
  Gemini returned the exact expected dominant color. The base64 wrapper keeps
  the binary PNG portable and reviewable while the test sends decoded bytes.
- Inline WAV input passed on all four available tested models. The test constructs a small,
  deterministic mono PCM16 recording containing three separated tones and
  requires the model to distinguish audible tones from silence. A preliminary
  exact-count assertion produced inconsistent counts across model generations
  and was correctly removed as a model-quality benchmark rather than a client
  compatibility contract.
- Inline AVI input passed on all four available tested models. The fixture is constructed in
  memory without external media tools and contains six seconds of solid red,
  green, and blue sections. Requiring the exact chronological sequence checks
  temporal visual understanding rather than merely accepting video bytes.
- Inline PDF input passed on all four available tested models through `LlmFileContent`. The
  dependency-free fixture contains a reference code and two quantities; the
  exact response requires both extraction and a small calculation, confirming
  that the attachment content reached the model as a document.
- Image generation was not sent through `ILlmClient`. Its response types cannot
  represent generated binary artifacts, and silently discarding Gemini's image
  output would produce a false test result. The tagged test is reserved for the
  provider-neutral GenerationClient described in the generation roadmap.
  Google's current Gemini pricing also lists both Gemini 3.1 Flash Image and
  Gemini 3.1 Flash Lite Image as unavailable on the free tier.

## Verification log

| Date | Provider and model | Capability | Result | Evidence |
| --- | --- | --- | --- | --- |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Baseline streaming | Pass | Exact `BAIZE_OK` response; finish reason `STOP` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Native tools | Pass | `echo_value` called once with schema-valid `value: baize-live` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Structured output | Pass | Schema-constrained JSON returned `value: baize-live` and `count: 3` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Native batch | Blocked | JSONL upload became `ACTIVE`; creation returned `FAILED_PRECONDITION` because Batch API is unavailable on the free tier |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Image input | Pass | Inline 128 by 128 PNG was identified as solid red; exact response `RED` |
| 2026-08-12 | Gemini image generation | Image generation | Blocked | No provider call made: GenerationClient is not implemented and Gemini image-generation models require the paid tier |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Baseline streaming | Pass | Exact `BAIZE_OK` response contract passed |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Native tools | Pass | Schema-valid `echo_value` tool-call contract passed |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Structured output | Pass | Schema-constrained JSON contract passed |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Image input | Pass | Inline PNG dominant-color contract passed |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Explicit thinking | Pass | Explicit low-effort thinking returned the expected arithmetic result with usage and diagnostics |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Audio input | Pass | Inline PCM16 WAV with audible tones was distinguished from silence; exact response `TONES` |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Audio input | Pass | Inline PCM16 WAV with audible tones was distinguished from silence; exact response `TONES` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Video input | Pass | Inline RGB24 AVI color sections were ordered correctly; exact response `RED GREEN BLUE` |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Video input | Pass | Inline RGB24 AVI color sections were ordered correctly; exact response `RED GREEN BLUE` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | PDF/file input | Pass | Inline generated PDF was parsed correctly; exact extracted and calculated response `ORBIT-417 21` |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | PDF/file input | Pass | Inline generated PDF was parsed correctly; exact extracted and calculated response `ORBIT-417 21` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Multi-turn tools | Pass | Selected lookup, consumed result, calculated restock amount `8`, called restock, then returned exact plan ID `PLAN-9` |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Multi-turn tools | Pass | Selected lookup, consumed result, calculated restock amount `8`, called restock, then returned exact plan ID `PLAN-9` |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Parallel tools | Pass | Returned weather and exchange-rate calls in one response, consumed both results together, then returned exact combined answer `MNL 31C USD/PHP 57.25` |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Parallel tools | Pass | Returned weather and exchange-rate calls in one response, consumed both results together, then returned exact combined answer `MNL 31C USD/PHP 57.25` |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite`, native `v1beta` | Core contracts | Pass | Baseline, native tools, sequential tools, parallel tools, and structured output all passed |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite`, native `v1beta` | Multimodal inputs | Pass | Image, strengthened audio, video, and PDF/file contracts all passed |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Core contracts except parallel follow-up | Pass | Baseline, native tools, sequential tools, and structured output passed |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Parallel tools | Blocked | Initial response returned both calls; follow-up hit the five-request-per-minute quota, and isolated retry later returned HTTP 503 high demand |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Multimodal inputs | Pass | Image, strengthened audio, video, and PDF/file contracts all passed |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Strengthened audio input | Pass | Inline PCM16 WAV with audible tones was distinguished from silence; exact response `TONES` |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite`, native `v1beta` | Strengthened audio input | Pass | Inline PCM16 WAV with audible tones was distinguished from silence; exact response `TONES` |
| 2026-08-12 | Gemini `gemini-2.5-flash-lite`, native `v1beta` | Model availability | Blocked | API returned HTTP 404: model is no longer available to new users |
| 2026-08-12 | Gemini `gemini-2.5-flash`, native `v1beta` | Model availability | Blocked | API returned HTTP 404: model is no longer available to new users |

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

## Recording future runs

After a live contract is run:

1. Record the exact provider, model identifier, native or compatible API
   style, API version, date, and capability.
2. Record `Pass`, `Fail`, or `Blocked`; do not generalize the result to sibling
   models without running them.
3. Add wire-dialect or token-budget observations when they affect client
   behavior.
4. Keep credentials and raw sensitive payloads out of this document. Raw HTTP
   diagnostics remain local and are ignored by source control.
