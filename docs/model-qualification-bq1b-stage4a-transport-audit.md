# BQ-1b Stage 4A transport audit — physical attempt qualification

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Starting commit: `d10ea1d79a7c390b764ccf97321732f4c087e80d` (Stage 3).
Branch: `implementation/baize-bq1b-stage4a`.
Runtime baseline: Windows 10.0.26200, .NET SDK 10.0.401,
Microsoft.NETCore.App 10.0.12; `System.Net.Http.dll` SHA-256
`b98ed0cb80dd54c33fc049f33af5448710752155bbd5cc8cbcd932e01f066801`.

**Disposition: QUALIFIED WITH RESTRICTIONS.** One exact transport profile
(`baize-sockets-http11-exact-v1`) has a reviewed upper bound of **one physical
HTTP request attempt per admitted inference invocation** for the closed set of
request properties and handler settings below. The runner enforces that bound at
the admission gate and reserves the corresponding worst-case budget before any
credential or dispatch. HTTP/2, HTTP/3, proxies, redirects, configured
authentication, cookies and custom middleware remain unsupported for live
eligibility. Provider billing semantics remain a separate Stage 4B dependency.
No actual model qualification was performed.

## Scope and selected profile

The selected target is the existing OpenAI-compatible adapter path through the
real Baize routed composition. No production adapter, router or HTTP client was
changed. The profile is runner-owned (`eng/Penghou.Baize.Qualification`) and is
recorded in every frozen plan subject and eligibility evidence.

| Profile field | Selected value |
| --- | --- |
| Profile id | `baize-sockets-http11-exact-v1` (revision `1`) |
| Adapter path | existing `Penghou.Baize.OpenAi` chat-completions adapter, routed by `LlmRouter` with one endpoint and `MaximumAttempts = 1` |
| Transport implementation | `SocketsHttpHandler` immediately below `QualificationAdmissionHandler` |
| Runtime | `.NET 10.0.12`, `System.Net.Http` 10.0.0.0, informational `10.0.12+95017c711e...`; assembly hash is part of the profile identity |
| HTTP protocol | HTTP/1.1, `HttpVersionPolicy.RequestVersionExact` set on each admitted request |
| Redirects | disabled (`AllowAutoRedirect = false`) |
| Automatic authentication | disabled (`Credentials = null`, `PreAuthenticate = false`) |
| Credential forwarding | none by the handler; the Baize adapter resolves only its frozen secret slot behind semantic admission |
| Proxy | disabled (`UseProxy = false`, `Proxy = null`) |
| Connection pool | one connection per server, 30-second connect timeout, 30-second idle timeout, 5-minute pooled lifetime |
| Retry/resilience middleware | none; runner `MaximumAttempts = 1`; unexpected retry above the guard is counted and denied |
| Cookies | disabled |
| Decompression | none (`DecompressionMethods.None`) |
| Endpoint restrictions | one frozen origin/port/base path per attempt; user info, query and fragment rejected at plan time and at the guard |
| Cancellation/deadlines | linked caller/attempt/deadline tokens; absolute attempt deadline; progress-based idle deadline; body disposal owns the concurrency slot |
| Physical-attempt upper bound | **1** per admitted inference invocation |

The profile identity is a canonical SHA-256 over the complete descriptor,
including the handler settings, protocol policy, reviewed bound and the actual
`System.Net.Http` assembly hash. The frozen plan stores that identity. Any
material change to the descriptor, runtime or bound changes the identity and
invalidates eligibility. A missing or different profile reference is recorded as
`transport-profile-unreviewed` and keeps the wire-attempt blocker.

## Call path and quantity boundaries

```
qualification case
  -> real Baize router (one endpoint, MaximumAttempts=1)
  -> OpenAI chat-completions adapter
  -> neutral Baize HTTP transport
  -> QualificationAdmissionHandler (lease, destination, request/response limits,
     protocol pin, physical-attempt cap, budget consent)
  -> SocketsHttpHandler (reviewed profile)
  -> TCP/TLS
  -> controlled server or provider
```

These quantities are deliberately separate:

| Quantity | Meaning | Stage 4A status |
| --- | --- | --- |
| Semantic model invocation | one run of a qualification contract through the routed adapter | one per probe |
| Application-admitted HTTP request | one `QualificationHttpDispatchAdmitted` record at the last application boundary | at most one per admitted attempt under the selected profile |
| Application-visible send | the guard admission immediately before the terminal handler | equal to admitted dispatches, not a wire count |
| Physical HTTP request attempt | one request message the client attempts to submit | **bounded to 1** under the reviewed profile |
| TCP connection attempt | a TCP connect, DNS selection or TLS handshake | not bounded by this profile; a connection retry is not a model request |
| Requests received by the server | complete requests parsed by the controlled server | at most one for a single admitted attempt in the probes |
| Potentially billable inference | provider-side billing decision | not established; depends on the selected provider's charging semantics |

The physical-attempt bound does **not** claim exactly-once execution, exactly-once
receipt or exactly-once billing. It claims a maximum of one HTTP request attempt
from one admitted application request under the stated profile. A TCP connection
may fail and be re-established or re-resolved; that is outside the HTTP-attempt
count and does not by itself create another inference request.

## Runtime source findings

The source audit used the tagged `dotnet/runtime` v10.0.12 files below. File
hashes are recorded in the evidence bundle; the installed runtime assembly hash
is also part of the profile identity.

| Source | Finding |
| --- | --- |
| `SocketsHttpHandler/HttpConnection.cs` (v10.0.12) | `_canRetry` starts `false` for every request. It is set `true` only when the server closes before any response byte has arrived and either the request has no content or `Expect: 100-continue` was used. Our profile rejects both conditions before the terminal handler. |
| `SocketsHttpHandler/HttpConnection.cs` | A failure after headers or body is sent maps to `RetryOnConnectionFailure` only when `_canRetry` is true; otherwise it maps to `NoRetry`. |
| `SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs` | The connection-failure retry loop is guarded by `e.AllowRetry == RetryOnConnectionFailure`; the described limit is `MaxConnectionFailureRetries = 3`. That loop is unreachable for the reviewed request shape because `_canRetry` is false. It is not used as the bound. |
| `SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs` | Version fallback (`RetryOnLowerHttpVersion`) and HTTP/2 stream-limit replay (`RetryOnStreamLimitReached`) are separate branches. The request is pinned to HTTP/1.1 exact, so HTTP/2/3 branches are not entered. |
| `SocketsHttpHandler/SocketsHttpHandler.cs` | A redirect handler is inserted only when `AllowAutoRedirect` is true; authentication is enabled only when credentials are configured; decompression is inserted only when configured. The selected profile disables all three. |
| `SocketsHttpHandler/HttpConnectionHandler.cs` | Request authentication is consulted only when the handler was constructed with credentials. `Credentials = null` therefore leaves a 401 response as an ordinary response, not a hidden retry. |
| `HttpClient.cs` / `HttpRequestMessage.cs` | `HttpClient.DefaultRequestVersion` applies to convenience-created requests, not to the existing `HttpRequestMessage` the adapter creates. The runner therefore sets `Version = 1.1` and `VersionPolicy = RequestVersionExact` on the actual request at the guard. |

The guard also rejects a request without content and a request carrying
`Expect: 100-continue`. Without those checks the HTTP/1.1 connection-failure
replay path would be reachable even for a POST. With them, the applicable
`SocketsHttpHandler` path for the selected profile has no request-replay branch.

This is a source-and-configuration proof, not a public-documentation inference.
The loopback probes exercise the corresponding success and failure boundaries on
the actual installed runtime.

## Deterministic probe matrix

All probes use the existing real Baize router/adapter/neutral transport over the
actual `SocketsHttpHandler`. The controlled backend is a local raw `TcpListener`
server (`tests/Penghou.Baize.Qualification.Tests/Stage4aLoopbackServer.cs`). No
DNS name, provider endpoint or real credential is used. Body and disposition
checks are synchronized with task completions and explicit deadlines, not with
arbitrary sleeps used as retry proof.

| Probe | Semantic | Guard admissions | Server requests | Result |
| --- | --- | --- | --- | --- |
| Normal HTTP/1.1 request | 1 | 1 | 1 complete, 1 connection | pass |
| Two sequential admitted attempts | 2 | 2 | 2 complete, 1 reused connection | pass |
| Streaming response | 1 | 1 | 1 complete; headers observed before body release | pass |
| Connection refused | 1 | 1 | 0 requests, 0 accepted connections | failure, no retry |
| Closed before request transmission | 1 | 1 | 0 requests, 1 connection | failure, no retry |
| Closed after request headers | 1 | 1 | 1 headers-only record, body incomplete | failure, no retry |
| Closed after complete request body | 1 | 1 | 1 complete request | failure, no retry |
| Timeout awaiting response headers | 1 | 1 | 1 complete request | bounded timeout, no retry |
| Partial response then disconnect | 1 | 1 | 1 complete request | failure, no retry |
| Idle body deadline | 1 | 1 | 1 complete request | bounded timeout, no retry |
| Read cancellation | 1 | 1 | 1 complete request | cancellation, no retry |
| Redirect response | 1 | 1 | 1 first-server request; 0 redirect-target requests | not followed |
| Authentication challenge | 1 | 1 | 1 complete request | not retried |
| Unexpected retry above the guard | 1 | 1 admitted + 1 denied | 1 complete request | denied at physical bound |
| Pool recovery after a failed request | 2 | 2 | 2 complete requests, 2 connections | two separately admitted attempts |
| Faulting body disposal | 1 | 2 | none (unit terminal) | slot released; second admission succeeds |
| Unreviewed/substituted profile | 0 | 0 | 0 | eligibility blocked |

`Stage4aLoopbackTests` and `Stage4aProfileTests` record the exact assertions. The
matrix distinguishes a server-observed request from a possible transmitted byte
sequence: a close-before-request probe proves no second request was attempted,
not that zero bytes ever left the client. The bound remains an upper bound.

## Physical-attempt bound

**For `baize-sockets-http11-exact-v1`, one admitted inference invocation cannot
produce more than one physical HTTP request attempt.**

Assumptions required for that statement:

1. The request is the existing POST inference request with a non-null content
   body.
2. The guard pins HTTP/1.1 exact and rejects `Expect: 100-continue`.
3. Redirects, configured authentication, cookies, proxy use and decompression
   remain disabled.
4. The runner-owned chain contains no retry middleware below the guard; the
   selected router uses one endpoint and `MaximumAttempts = 1`.
5. The selected runtime and `System.Net.Http` assembly hash match the frozen
   profile identity.
6. The subject uses the exact reviewed profile reference and no HTTP/2 or HTTP/3
   negotiation.
7. The provider cannot cause a new request from the client solely by returning
   an HTTP status, challenge or redirect; the client does not follow such
   responses.

The bound is enforced twice:

- **Guard:** a second dispatch for the same attempt is denied with
  `physical-attempt-bound-exceeded`, even when the global policy still has
  request allowance.
- **Budget:** reservation units are scaled by the accepted bound; the one
  reviewed profile reserves one model call, one HTTP request and the complete
  per-attempt token/cost envelope before credentials or dispatch.

Unreviewed profiles, loopback-only profiles, and any profile without a matching
identity remain blocked. Loopback probes use the same handler but are never live
qualification targets (`loopback-not-live-eligible`).

## Exclusions and remaining gaps

- HTTP/2, HTTP/3, ALPN downgrade, GOAWAY stream replay and HTTP/2 stream-limit
  retry are not qualified. They are explicitly unsupported for live eligibility.
- Proxy configuration, proxy authentication, redirect following, configured
  credentials and automatic decompression are not qualified.
- The proof covers the existing OpenAI-compatible chat-completions adapter
  only. Claude, Gemini and Ollama adapters still have no scripted live terminal
  qualification.
- TCP connection attempts, DNS answers, TLS handshakes and OS-level socket
  retries are not counted as HTTP request attempts and are not reflected in the
  bound. They may add latency and connection attempts, not an additional model
  request under this profile.
- Wire-byte and preallocation bounds remain unresolved (S0-G6). Decoded-byte
  ceilings are enforced, but they are not a claim about network bytes or parser
  allocation.
- Provider billing semantics, account allowance, token completeness and price
  correctness remain separate facts for Stage 4B. A transport-qualified profile
  is not automatically authorized for paid execution.
- No model compatibility claim (chat, streaming, tools or structured output) is
  made by this stage.

## Process deviation

During implementation, an existing Stage 3 privacy test still called
`run --allow-live` for the now transport-eligible OpenRouter profile. That
intermediate local test run used a sentinel environment value, not a real
credential, and exited through the failure path without producing qualification
evidence. The test was corrected to exercise a still-ineligible profile and the
final suite contains no live-profile execution.
Because the test had a sentinel credential and the request may have left the
machine, this stage does **not** claim zero outbound requests during development.
No valid credential or paid qualification request was authorized or intentionally
made; the incident is retained here as a process finding for Stage 4B.
