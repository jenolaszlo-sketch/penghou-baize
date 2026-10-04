# Governable transport: source inventory

Reviewed 2026-10-04 against Baize `442397ecf25f335309e7f3ab121e9c4359fda100`
and Penghou `e451b71c4927d6106834b8a49f145411bdd05648`, including the existing
documentation changes in the working tree. Read-only review; no live providers,
secrets or sandbox experiment were used. Source spec SHA-256: cbbc66c60b0162693ab19532bf56f01cd3a5a92623dda0e89f6028e27b134142.

This closes the initial source reconnaissance portion of
[GM-0](governable-model-http-plan.md); the contract ADR, exhaustive per-method
coverage matrix and baseline qualification still remain open.

| Existing boundary | Source | Migration implication |
| --- | --- | --- |
| Streaming chat | [LlmClientBase](../src/Penghou.Baize/LlmClientBase.cs), [ILlmClient](../src/Penghou.Baize/ILlmClient.cs) | Shared base shapes/authenticates HTTP and calls the injected factory's named `llm` client. Preserve SSE/NDJSON, rate limits, stream lifetime and integrity; add a semantic check before credential loading/dispatch. |
| Completion | [ILlmCompletionClient](../src/Penghou.Baize/ILlmCompletionClient.cs) and provider implementations | Enumerate native completion paths separately; collecting a stream is not proof that all native calls cross the seam. |
| Routing and existing decorators | [router](../src/Penghou.Baize.Router/LlmRouter.cs), [registration](../src/Penghou.Baize.Router/Extensions/ServiceCollectionExtensions.cs), [deferred clients](../src/Penghou.Baize.Router/DeferredEndpointClients.cs) | Decorators already exist but are Baize-specific; checking at registration/client creation is insufficient. Every actual enumeration/attempt requires a fresh transport check. Deferred clients can load secrets before invoking a client decorator: move governed secret resolution after semantic admission. |
| Endpoint identity | [LlmClientMetadata](../src/Penghou.Baize/LlmClientMetadata.cs) | Provider, wire-model, endpoint URI and endpoint ID already exist. Preserve these rather than authorizing a router alias alone. |
| Native batches | [IBaizeBatchClient](../src/Penghou.Baize/IBaizeBatchClient.cs), [base](../src/Penghou.Baize/BaizeBatchClientBase.cs), [OpenAI](../src/Penghou.Baize.OpenAi/OpenAiBatchClient.cs), [Claude](../src/Penghou.Baize.Claude/ClaudeBatchClient.cs), [Gemini](../src/Penghou.Baize.Gemini/GeminiBatchClient.cs) | Submit, status, results and cancel are independent externally consequential calls; upload/download paths and each item context need coverage. |
| Generated media | [generation base](../src/Penghou.Baize/Generation/GenerationClientBase.cs), [contract](../src/Penghou.Baize/Generation/IGenerationClient.cs), provider packages | OpenAI/Gemini/Runway/fal.ai have asynchronous and artifact lifecycles outside chat. Shared-base-only migration leaves gaps. |
| HTTP composition | [BaizeHttp](../src/Penghou.Baize/BaizeHttp.cs), [registration](../src/Penghou.Baize/BaizeServiceCollectionExtensions.cs) | Existing injectable factory and handlers are useful implementation infrastructure, but not neutral semantic authorization. Preserve named client, timeout, diagnostics and response ownership when adapting HTTP. |
| Metadata | [LlmRequest](../src/Penghou.Baize/LlmRequest.cs) | Copied host-neutral metadata already exists and must stay off provider wire requests. It establishes no authority. |
| Failure handling | [failure kinds](../src/Penghou.Baize/LlmClientFailureKind.cs), [router](../src/Penghou.Baize.Router/LlmRouter.cs) | Existing provider Authorization is distinct from Availability/RateLimit. Neutral host denial must bypass fallback; a generic HttpRequestException could trigger fallback. |
| Existing shared web API | [IO contracts](../../Penghou/src/Penghou.IO.Abstractions/Contracts.cs) | `IWebResourceReader` is credential-free bounded GET, not provider POST/upload/streaming. No model transport contract project exists yet. |

The reviewed source/project files contain no Hufu references. Preserve that
property with a permanent direct/transitive dependency test; this textual check
alone is not package-closure qualification. No hidden `new HttpClient()` was
found in the provider search, but this does not prove complete governance.

The smallest development slice is the neutral contract decision/package stage,
then chat streaming/default/denial integration against fake providers. Reuse
existing factory/handler, provider and router fixtures. The whole refactor also
requires batch, generation, Extensions.AI, uploads, asset access, provider retries,
repair and fallback coverage; do not graduate after a chat decorator prototype.

## GM-0 implementation review — 2026-10-04

The detailed [consumer operation matrix](../../Penghou/docs/model-http-consumer-matrix.md)
and [ADR 0004](../../Penghou/docs/decisions/0004-model-http-transport-contracts.md)
now close the contract design/closure decisions for the initial candidate profile.
The [release checkpoint](../../Penghou/docs/model-http-release-handoff.md) records
baseline/contract/package qualification separately from future Baize migration.
Generic typed payloads preserve Baize's type ownership, but its consumer profile
must snapshot and validate them before policy/dispatch. Keep cache hits and
cache writes distinct. Deferred-secret ordering, all per-item bindings and all
HTTP/upload/result routes remain GM-2/3/4 implementation obligations.
