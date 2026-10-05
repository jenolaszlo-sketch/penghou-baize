# Governable chat and HTTP transports

Updated 2026-10-05. This is the published GM-2 chat-first milestone. Baize core
consumes exact published `Penghou.Model.Abstractions` and
`Penghou.Http.Abstractions` **0.1.0-preview.1** packages. No Hufu dependency is
introduced. Baize 0.3.0-preview.7 is published and indexed for this chat-first milestone. Full batch/generation governance remains a later milestone.

## Host composition

`LlmClientBase.ModelTransportFactory` accepts an
`IBaizeModelTransportFactory`. Its generic `Wrap` and `WrapStreaming` methods
receive a trusted `ModelTarget` and the default typed transport. A host can
decorate that transport with policy checks, or replace it with a broker that
returns `LlmResponse` / `LlmStreamEvent` payloads. The default
`PassThroughBaizeModelTransportFactory.Instance` needs no authority service.
Existing provider constructors remain available.

For router composition, register `IBaizeModelTransportFactory` in the DI
container. The router checks the selected endpoint before resolving its secret
or constructing its provider client. Built-in direct chat clients identify their
configured dispatch URI; router targets use the configured endpoint ID. Bind
these host-selected IDs to actual destinations in your policy configuration.
Custom `LlmClientBase` subclasses should call the protected
`ConfigureTransportEndpoint` method. Its fallback identity is `default`, which
must not be treated as a qualified destination.

Register a replacement `Penghou.Http.Abstractions.IHttpTransport` alongside
`AddBaizeTransport`. The default registration uses `TryAddSingleton`, preserving
an earlier host registration. A direct provider can use
`BaizeHttp.CreateClientFactory(httpTransport)` where an `IHttpClientFactory`
is required. Existing factories are bridged automatically in the built-in
chat, batch and generation base classes.

`model.invoke` admission and arbitrary `http.request` permission remain separate
host decisions. Give a trusted model implementation its own private HTTP
transport when its provider requests need different permissions from caller HTTP.
Registering one global denying HTTP transport also denies provider HTTP;
caller flags, URLs and context IDs cannot grant a privileged channel.

## Context, admission and errors

Set `LlmRequest.ExecutionContext` and `UsageIntent` using the neutral
`ModelExecutionContext` and `ModelUsageIntent` types. `LlmPromptBuilder` exposes
the same properties. With Extensions.AI, use `ChatOptions.AdditionalProperties`
with `BaizeChatClient.ExecutionContextKey` and `UsageIntentKey`; values must have
the corresponding neutral type. They are preserved through chat routing and
never automatically serialized into a provider prompt or wire metadata.
The host authenticates context separately; descriptive IDs confer no authority.

Streaming admission runs when enumeration starts, and runs again for every
new enumeration. Router retry/fallback attempts get fresh invocations and
increasing attempt numbers. `CompleteAsync` has one `Complete` admission;
the built-in default collects the existing provider stream internally. It does
not add a new native non-streaming provider endpoint. Router custom native
completion implementations are checked before construction and secret lookup.

`ModelAccessDeniedException`, `ModelBudgetExceededException` and HTTP authority
denial propagate and stop default fallback. Availability failures may retry or
fall back with a fresh check. Provider authentication/403, throttling and protocol
failures retain their existing meaning. Response request-ID or trusted target /
operation / context / usage-intent / attempt substitution is rejected.

Reported token counts, cache hits/misses, thinking tokens, decimal cost and
currency survive the transport mapping. Missing usage remains unknown; counts
too large for Baize's integer usage fields fail explicitly rather than truncate.

## Supported payload profile

Chat requests are snapshotted before admission and again before trusted default
dispatch. Supported content comprises the existing exact text, reasoning, tool
call/result and image/audio/video/file types, with inline bytes, HTTP(S) URIs
or provider file references. Collections become read-only and inline data getters
return detached bytes. Unknown derived content/media types and executable or
mutable arbitrary metadata objects are rejected before policy or dispatch.

Limits are 1,024 messages, 8,192 content parts, 256 tools, 32 metadata entries,
4 MiB per text/schema field and 64 MiB aggregate payload (including inline data).
Metadata accepts null, bounded strings, scalar primitives and cloned bounded
JSON. Provider continuations permit 32 entries with 64-byte keys and 1,024-byte
values. Repair histories and validation diagnostics have explicit collection
and string bounds. See `BaizeModelTransport` and profile tests for exact bounds;
custom profiles need explicit design and qualification before acceptance.

## HTTP ownership and limits

The bridge converts legacy provider requests into finite binary or multipart
data before calling `IHttpTransport`. Multipart permits at most 64 parts;
default transport counts encoded framing against the body limit. Response
identity, headers, chunk size, total decoded bytes and terminal chunks are
validated. Dispose the returned response to release its owned body.
Cancellation and deadlines cover response reads after headers arrive.

Default request/response limits are 64 MiB. Larger upload profiles permitted
by the shared contract are **not qualified by this milestone**; snapshots
currently copy finite buffers. Large uploads need a separately reviewed memory
and ownership profile before GM-3 completion.

The named default HTTP client has a 100-second timeout. Its configured value
is preserved through response body reads, including host-configured longer
timeouts. The automatic default bridge permits up to the shared one-day ceiling;
the underlying named-client deadline remains effective. A custom neutral
transport bridge defaults to 100 seconds. `WithRequestTimeout` overrides the
default backend factory's timeout and covers body reads too; an endpoint can
extend as well as shorten the named default. As in the previous API, factories
used with explicit timeout overrides must return fresh clients.
Reused legacy clients retain
their timeout. The default DI handler disables automatic redirects and cookies;
the default transport supports zero redirects. A host supplying other handlers
must qualify their redirect, destination, decompression and credential behavior.
These library seams do not enforce process/network isolation against arbitrary
custom code or sockets.

## Qualification and remaining work

Core, OpenAI, Claude, Gemini, Ollama, router and Extensions.AI chat surfaces
use the semantic seam. Shared base HTTP dispatch also covers built-in batch and
generation requests. **Batch and generation do not yet have semantic model
admission**, including submit/status/results/cancel, per-item context, uploads
and asset retrieval. These remain GM-3, followed by executor/custom-provider
closure under GM-4 and the full B1-B12 qualification under GM-5.

The dependency check verifies exact published neutral package versions in all
12 source project closures and absence of Hufu. The package consumer check packs
a local Baize core candidate, restores both neutral packages from public NuGet
in a fresh cache, and exercises default HTTP and semantic denial without network
dispatch. This qualifies core consumption, not every Baize package release.

CI retains full regression/coverage gates and adds transport adoption and fresh
package consumers on Windows, Ubuntu and macOS for net8.0/net10.0. Local evidence
and the next gate are recorded in the implementation handoff. The
[release CI](https://github.com/jenolaszlo-sketch/penghou-baize/actions/runs/37251898339)
passed all jobs. A post-publication fresh consumer restored all 12 Baize packages
at 0.3.0-preview.7 and both neutral dependencies from public NuGet only, verified
no Hufu in the closure, and exercised core semantic denial and HTTP ownership on
net8.0/net10.0. Referencing every package proves delivery/dependency compatibility;
it does not close the remaining batch/generation semantic coverage.
