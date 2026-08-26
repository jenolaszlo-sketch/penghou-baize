# Generation contract matrix

Completed Phase 2 comparison of the generation-capable providers that validate
the stable `IGenerationClient` contracts in `Penghou.Baize.Generation`. The
matrix records operation states, synchronous and queued behavior, idempotency,
cancellation, progress, inputs, outputs, errors, rate limits, candidate counts,
and asset URL expiry per provider API.

Evidence is tagged so nothing is asserted without a source:

- **Implemented** — the behavior is exercised by the named Baize adapter and
  its deterministic tests.
- **Probe** — verified by opt-in live provider evidence in this repository;
  this tag is independent of whether a deterministic adapter also exists.
- **Docs** — taken from provider documentation and retained where Baize cannot
  verify the behavior deterministically.

## OpenAI — image generation and editing

`/images/generations` and `/images/edits`. Evidence: **Implemented**.

| Aspect | Behavior |
| --- | --- |
| Operation states | Immediate completion only; the submission call returns `Succeeded`. No queue. |
| Synchronous / queued | Synchronous (request/response). |
| Idempotency | None documented; nothing to retry blindly. |
| Cancellation | Not applicable (immediate completion). |
| Progress | None. |
| Inputs | Text prompt; edits additionally accept one or more images as inline bytes or an absolute URI (multipart). |
| Outputs | Image assets as inline base64 data or a temporary URL; `revised_prompt` may accompany the result. |
| Errors | HTTP status with an `{error:{message,type,param,code}}` body; mapped to the `GenerationErrorKind` taxonomy. |
| Rate limits | Per-model, not encoded in the wire protocol. |
| Candidate counts | `n` parameter; the Baize adapter caps it through `MaximumCandidates` (for example `gpt-image-1` accepts only `n=1`). |
| Asset URL expiry | `url` outputs are temporary signed URLs; `b64_json` is immediate. Both are exposed as distinct asset sources. |

## OpenAI — video (Sora)

`/videos`, `GET /videos/{id}`, `DELETE /videos/{id}`. Evidence: **Implemented**.

| Aspect | Behavior |
| --- | --- |
| Operation states | `queued`, `in_progress`, `completed`, `failed`; mapped to `Queued`, `Running`, `Succeeded`, `Failed`. |
| Synchronous / queued | Queued. Submission returns a handle; `GetAsync` polls the status endpoint. |
| Idempotency | None documented. |
| Cancellation | Supported by `DELETE /videos/{id}`; advertised through `GenerationFeature.Cancellation` and gated at the client. |
| Progress | Optional numeric `progress` (0–1) surfaced on the operation when present. |
| Inputs | Text prompt. |
| Outputs | Video file URL(s) (`output` or `content[].url`), typically `video/mp4`. |
| Errors | A structured `error` (code + message) on the video document; mapped to `GenerationErrorKind`. |
| Rate limits | Per-model and account tier, not encoded. |
| Candidate counts | Single video per operation today; the request model carries `n` but providers currently accept one. |
| Asset URL expiry | Output URLs are temporary; the operation preserves the provider URL but does not download or pin them. |

## OpenAI — speech

`/audio/speech`. Evidence: **Implemented**.

| Aspect | Behavior |
| --- | --- |
| Operation states | Immediate completion only (`Succeeded`). |
| Synchronous / queued | Synchronous. |
| Idempotency | None documented. |
| Cancellation | Not applicable. |
| Progress | None. |
| Inputs | Text prompt, a voice, and a response format. |
| Outputs | Inline binary audio (mp3, opus, aac, flac, wav, pcm). |
| Errors | HTTP error mapping like the other OpenAI endpoints. |
| Rate limits | Per-model, not encoded. |
| Candidate counts | One audio result per request. |
| Asset URL expiry | Not applicable — inline bytes. |

## Gemini — Interactions API image generation

`POST /v1beta/interactions` with an image-capable model such as
`gemini-3.1-flash-lite-image`. Evidence: **Implemented** (Baize adapter) plus
**Probe** (the opt-in `GeminiGenerationProviderProbeTests`; see
`docs/providers/gemini.md`).

| Aspect | Behavior |
| --- | --- |
| Operation states | Synchronous; a completed interaction maps to `Succeeded`. A non-`completed` status maps to `Unknown` rather than being misclassified. |
| Synchronous / queued | Synchronous chat-shaped request; an image-capable model returns image parts in the response. |
| Idempotency | None; submissions are not retried by the adapter. |
| Cancellation | Not applicable (no task lifecycle); `CancelAsync` is rejected before a provider call. |
| Progress | None. |
| Inputs | Text prompt plus optional image parts (inline base64 or public URL) for image editing. |
| Outputs | Inline image parts with a MIME type and base64 data; mapped to inline assets. |
| Errors | Provider error bodies mapped to `GenerationErrorKind`; an empty image response surfaces `GenerationFailed`. |
| Rate limits | Account tier dependent; not encoded. |
| Candidate counts | Not supported; a count greater than one is rejected before submission. |
| Asset URL expiry | Inline data only. |

## Runway — Tasks API (text-to-video, image-to-video)

`POST /text_to_video`, `POST /image_to_video`, `GET /tasks/{id}`,
`DELETE /tasks/{id}`. Evidence: **Implemented** (Baize adapter; see
`Penghou.Baize.Runway`) plus **Docs**.

| Aspect | Behavior |
| --- | --- |
| Operation states | `PENDING`, `THROTTLED`, `RUNNING`, then a terminal `SUCCEEDED`, `FAILED`, or `CANCELLED`. `THROTTLED` means the task is stored but not yet enqueued (concurrency limit) and should be treated like `PENDING`. |
| Synchronous / queued | Fully asynchronous. Submission returns a task id; poll `/tasks/{id}`. Runway recommends polling no faster than every 5 seconds. |
| Idempotency | No documented idempotency key on submission. |
| Cancellation | `DELETE /tasks/{id}` cancels a pending task or deletes a completed one; cannot be undone. |
| Progress | Numeric `progress` (0–1) on the task document while running. |
| Inputs | Text-to-video: `promptText` (≤ 1000 characters), optional `duration` (5 or 10) and `ratio` presets. Image-to-video additionally accepts an input image. |
| Outputs | One or more temporary asset URLs (`output`), populated only when `SUCCEEDED`. |
| Errors | `400`/`401`/`404`/`429` with a machine-readable `code` and `error` message; `FAILED` tasks carry a `failure` description. |
| Rate limits | `429` responses; concurrency limits surface as `THROTTLED` rather than an error. |
| Candidate counts | One task per submission; no candidate count in the gathered documentation. |
| Asset URL expiry | Output URLs are temporary; the documented TTL is not published in the sources gathered here. |

Authentication uses a Bearer API key plus a required `X-Runway-Version:
2024-11-06` header.

## fal.ai — queue API

`POST https://queue.fal.run/{model}`, status/result/cancel sub-routes under
`requests/{request_id}`. Evidence: **Implemented** (Baize adapter; see
`Penghou.Baize.Fal`) plus **Docs**.

| Aspect | Behavior |
| --- | --- |
| Operation states | `IN_QUEUE`, `IN_PROGRESS`, `COMPLETED`; a `COMPLETED` status carries `status_code` (`OK`/`ERROR`) and `metrics.inference_time`. |
| Synchronous / queued | Multiple execution styles over the same endpoint: `submit` (queue, returns immediately), `subscribe` (queue plus automatic polling), `run` (direct), `stream` (SSE), and `realtime` (WebSocket). |
| Idempotency | No submission idempotency key; webhook retries are deduplicated by `request_id`. |
| Cancellation | `PUT .../requests/{request_id}/cancel`. |
| Progress | Queue `position` for `IN_QUEUE`; SSE status streaming for live updates. |
| Inputs | Model-specific JSON arguments with an arbitrary per-model schema. |
| Outputs | Model-specific JSON; image/video models typically return URLs (lifecycle preferences are controllable via `x-fal-object-lifecycle-preference`). Storage-backed assets are documented as `{url, content_type, file_name, file_size}`; the Baize adapter preserves those fields on each `GeneratedAsset`. |
| Errors | Error status bodies and webhook `status: "ERROR"` payloads. |
| Rate limits | Queue priority header (`x-fal-queue-priority`, `low`/`normal`); server-side `start_timeout` via `x-fal-request-timeout`. |
| Candidate counts | Model-dependent, not part of the transport contract. |
| Asset URL expiry | Object lifecycle preference controls retention/expiry for storage-backed outputs. |

## Implications for the common contracts

- **Immediate and queued providers must share one surface.** OpenAI images and
  speech return `Succeeded` synchronously; OpenAI video, Runway, and fal.ai are
  queued. The `IGenerationClient` snapshot model (`SubmitAsync` returning
  `Queued` with a handle) already absorbs both, as required by the roadmap.
- **Progress is optional and varies in shape.** OpenAI video surfaces a numeric
  fraction; Runway reports `progress` 0–1; fal.ai reports queue position and
  SSE updates; Gemini image generation exposes none. `GenerationOperation.Progress`
  stays optional and provider metadata carries the rest.
- **Cancellation support is real but inconsistent.** OpenAI video, Runway, and
  fal.ai all support it via different verbs; immediate providers do not. The
  `GenerationFeature.Cancellation` flag keeps this truthful.
- **Idempotency is largely absent at submission time.** Only fal.ai's webhook
  `request_id` hints at deduplication, and none of the surveyed providers
  documents a submission idempotency key. Ambiguous-submission handling must
  therefore remain conservative; automatic submission retries cannot be safe.
- **`THROTTLED` (Runway) shows rate limiting can appear as a state, not an
  error.** A common adapter must not classify it as a failure.
- **Input schemas diverge sharply.** Text prompts are portable; image inputs,
  durations, ratios, and arbitrary model arguments are not. Keeping
  provider-native options on provider clients preserves fidelity.
- **Asset sources differ.** Inline data (OpenAI images/speech, Gemini),
  temporary URLs (OpenAI video, Runway), and storage-backed URLs (fal.ai) all
  occur. The `GeneratedAssetSource` hierarchy models inline data and URLs, and
  `ProviderGeneratedAssetSource` accommodates provider-owned file identifiers
  for fal.ai/Replicate. The fal adapter now preserves the provider's documented
  per-asset metadata (`content_type`, `file_name`, `file_size`) from the output
  document instead of inferring a content type from the URL extension alone.
- **Expiry information is unevenly documented.** Only fal.ai exposes an explicit
  lifecycle preference; Runway's output TTL is not published in the gathered
  sources. `GeneratedAsset.ExpiresAt` stays populated only when a provider
  conveys an expiry; otherwise asset expiry remains provider metadata rather
  than a guarantee.
