# Live provider verification log

This append-only log records live contract runs against exact provider, model,
API-style, and date combinations. See the
[compatibility matrix](live-provider-compatibility.md) for the current summary
and provider-specific conclusions.

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
| 2026-08-12 | DeepSeek `deepseek-v4-flash`, OpenAI-compatible `/v1` | Core contracts | Pass | Baseline, native tools, sequential tools, parallel tools, and explicit thinking passed |
| 2026-08-12 | DeepSeek `deepseek-v4-pro`, OpenAI-compatible `/v1` | Core contracts | Pass | Baseline, native tools, sequential tools, parallel tools, and explicit thinking passed |
| 2026-08-12 | DeepSeek `deepseek-v4-flash`, Claude-compatible `/anthropic` | Core contracts | Pass | Baseline, native tools, sequential tools, parallel tools, explicit thinking, and tool-backed structured output passed |
| 2026-08-12 | DeepSeek `deepseek-v4-pro`, Claude-compatible `/anthropic` | Core contracts | Pass | Baseline, native tools, sequential tools, parallel tools, explicit thinking, and tool-backed structured output passed |
| 2026-08-12 | DeepSeek V4 Flash and Pro, OpenAI-compatible `/v1` | Native JSON Schema output | Unavailable | Both returned HTTP 400 because `response_format.type = json_schema` is unavailable |
| 2026-08-12 | DeepSeek V4 Flash and Pro, OpenAI-compatible `/v1` | JSON object mode | Pass | Both returned valid JSON with the prompted `value` and `count` shape using `response_format.type = json_object` |
| 2026-08-12 | DeepSeek V4 Flash and Pro, OpenAI-compatible `/beta` | Strict tool schema | Pass | Both emitted a schema-compliant `record_order` call with `function.strict = true`; beta schema-validation behavior differed from parts of the published subset |
| 2026-08-12 | DeepSeek `deepseek-v4-flash`, OpenAI-compatible `/v1` with `DeepSeek` dialect | Tool-backed structured output | Pass | Forced synthetic tool returned schema-valid `value: baize-live` and `count: 3`; adapter repackaged arguments as canonical content |
| 2026-08-12 | DeepSeek `deepseek-v4-pro`, OpenAI-compatible `/v1` with `DeepSeek` dialect | Tool-backed structured output | Pass | Forced synthetic tool returned schema-valid `value: baize-live` and `count: 3`; adapter repackaged arguments as canonical content |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Explicit thinking | Blocked | No response headers arrived before the configured 100-second `HttpClient` timeout; no protocol incompatibility established |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Explicit thinking | Pass | Low-effort thinking returned `323` with provider-reported thinking usage |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite`, native `v1beta` | Explicit thinking | Pass | Low-effort thinking returned `323` with provider-reported thinking usage |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Parallel tools | Pass | Previously free-tier-blocked two-call round trip completed after paid access was enabled |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Native batch | Pass | One-item durable batch completed in about three minutes; current direct `responsesFile` envelope and download endpoint were fixed and verified |
| 2026-08-12 | Gemini `gemini-3.5-flash-lite`, native `v1beta` | Native batch | Pass | One-item durable batch completed and returned correlated `BAIZE_BATCH_OK` content |
| 2026-08-12 | Gemini `gemini-3.5-flash`, native `v1beta` | Native batch | Pass | One-item durable batch completed and returned correlated `BAIZE_BATCH_OK` content |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite`, native `v1beta` | Native batch | Pass | One-item durable batch completed and returned correlated `BAIZE_BATCH_OK` content |
| 2026-08-12 | Gemini `gemini-3.6-flash`, native `v1beta` | Explicit thinking retry | Pass | Isolated retry returned `323` with thinking usage in two seconds after the live harness timeout was raised from 100 to 300 seconds |
| 2026-08-12 | Gemini `gemini-2.5-flash`, native `v1beta` | Paid-tier availability retry | Blocked | Still returned HTTP 404 stating the model is unavailable to new users through `generateContent` |
| 2026-08-12 | Gemini `gemini-2.5-flash-lite`, native `v1beta` | Paid-tier availability retry | Blocked | Still returned HTTP 404 stating the model is unavailable to new users through `generateContent` |
| 2026-08-12 | Gemini `gemini-2.5-pro`, native `v1beta` | Paid-tier availability | Blocked | Returned HTTP 404 stating the model is unavailable to new users through `generateContent` despite appearing in model discovery |
| 2026-08-12 | Gemini `gemini-3.1-pro-preview`, native `v1beta` | Core contracts | Partial | Baseline, sequential tools, structured output, and explicit thinking passed; simple and parallel tools each returned provider `MALFORMED_FUNCTION_CALL` twice |
| 2026-08-12 | Gemini `gemini-3.1-pro-preview`, native `v1beta` | Multimodal inputs | Pass | Image, audio, video, and PDF/file contracts all passed |
| 2026-08-12 | Gemini `gemini-3.1-pro-preview`, native `v1beta` | Native batch | Pass | One-item durable batch completed in about four minutes and returned correlated `BAIZE_BATCH_OK` content |
| 2026-08-12 | Gemini `gemini-3.1-flash-lite-image`, Interactions `v1beta` | Image generation provider probe | Pass | Paid request completed in about four seconds and returned a MIME-typed, non-empty, base64-decodable image artifact; GenerationClient remains unimplemented |

## Recording future runs

After a live contract is run:

1. Append the exact provider, model identifier, native or compatible API
   style, API version, date, and capability.
2. Record `Pass`, `Fail`, or `Blocked`; do not generalize the result to sibling
   models without running them.
3. Record concise evidence and add broader wire-dialect conclusions to the
   compatibility page when they affect client behavior.
4. Keep credentials and raw sensitive payloads out of this document. Raw HTTP
   diagnostics remain local and are ignored by source control.
