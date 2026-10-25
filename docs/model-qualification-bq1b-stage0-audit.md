# BQ-1b Stage 0: execution boundary feasibility

Date: 2026-10-25 (Asia/Manila). Investigator: Sol/Codex.
Repository: `jenolaszlo-sketch/penghou-baize`.
Audited production/design base: **`3986dde792f6af49f4f3a51e27db1a6d721d691f`**.
Dedicated branch: `investigation/baize-bq1b-stage0`.

**Disposition: READY WITH RESTRICTIONS.** Stage 1's offline registry/planner is
technically feasible using existing services. No production API change is a
prerequisite for offline Stages 1–3. Actual wire-attempt admission/counting is
not established by the existing HTTP port or a delegating handler; real network
profiles must remain ineligible for a hard wire-attempt allowance until that
gap is resolved for the exact backend. Unknown economics also block paid
execution. These restrictions permit the planned two-fake-model milestone.

This is a bounded feasibility audit, not another BQ-1a review or authorization
to implement the runner. BQ-1a remains ACCEPT WITH FOLLOW-UP; F1 remains separate
adapter work. No production sources, accepted contracts, package references,
old review evidence or existing test projects are modified. All new executions
use in-memory HTTP handlers; no sockets or real provider services are invoked.

Read the [implementation plan](model-qualification-bq1b-implementation-plan.md),
[execution contract](model-qualification-execution.md),
[evidence specification](model-qualification-evidence.md),
[decisions](model-qualification-decisions.md),
[BQ-1a closure](model-qualification-bq1a-closure.md) and accepted contracts.
The [Stage 0 decisions](model-qualification-bq1b-stage0-decisions.md) resolve the
five requested choices within the bounded scope. They do not approve live use.

## Call paths and actual enforcement points

Locations refer to the audited base. A reported count is not an admission gate.

| Path / source locations | Demonstrated behavior and remaining responsibility |
| --- | --- |
| `Router/Extensions/ServiceCollectionExtensions.cs`:74–112,126–207 | `AddLlmRouting` validates configuration, registers modules and a reloading state. Building the lookup resolves provider defaults/capabilities and creates deferred client wrappers, not native chat clients. Allowlist built-in assemblies before loading them. |
| `Router/ReloadingLlmRoutingState.cs`:49,192–266 | Public routing follows replaceable configuration snapshots. It does not freeze a qualification subject. A host-supplied fixed `IOptionsMonitor<LlmRoutingOptions>` is already supported by `ServiceCollectionExtensions.cs`:259–269; deep-copy its data and retain a binding digest. |
| `Router/LlmRouter.cs`:418–454,498–638; `LlmRouterRetryOptions.cs`:8–16 | MaximumAttempts counts passes over endpoints. One still permits fallback across different endpoints. The selected request gets a new TransportAttempt for each client execution. Restrict each trial to one endpoint/model and no fallback/named/strategy chain. |
| `Router/DeferredEndpointClients.cs`:153–179,198–223 | Routed `IBaizeModelTransportFactory.WrapStreaming` gates enumeration before native client/secret resolution. Target, operation, context, usage intent and attempt are checked against the request. This is the preferred one admitted semantic boundary. Install the guard here, not again on the inner client's factory. |
| `Router/DeferredEndpointClients.cs`:29–33,107–134; `ServiceCollectionExtensions.cs`:272–299,440–455 | Deferred creation resolves credentials and constructs a native client. Cancellation cancels the waiter, not the underlying context task; the secret resolver is called without the caller token. A late secret can still construct a client after cancellation. |
| `Router/ReloadingLlmRoutingState.cs`:169–181,280–343 | Endpoint validation directly obtains native chat/batch clients, resolving secrets without semantic admission. It is not the offline inventory validator. Metadata lookup is the safe existing resolution path for planning. |
| `Transport/LlmClientBase.ModelTransport.cs`:35–99,112–173 | Direct clients also expose the model factory. Default completion collects the internal stream; it does not prove a provider-native completion API. Direct construction already receives credentials, so this is not the selected pre-credential execution entry point. |
| `LlmClientBase.cs`:44,85–145,146–215 | Built-in chat clients use `EnsureTransportFactory`, validate/build/authenticate a request, send once through the supplied client, parse events and own response/stream disposal. There is no application retry loop in this shared path. Error messages can include response text; never persist them by default. |
| `BaizeHttp.cs`:51–85; `Transport/BaizeHttpTransport.cs`:140–192 | Provider legacy sends cross the injected neutral port. It snapshots the body with finite limits, creates an HTTP request ID and checks response identity. This factory path supplies null `HttpRequestContext`; semantic correlation is not automatically carried into that field. |
| `Transport/BaizeHttpTransport.cs`:19–47,49–90,102–121 | Default backend builds a request and calls the named HttpClient with ResponseHeadersRead. Header limits are checked after header materialization; body reads check cumulative decoded bytes and own response disposal/deadline. No account concurrency or request-count admission exists here. |
| `BaizeServiceCollectionExtensions.cs`:19–36 | Default backend is HttpClientHandler with redirects/cookies disabled. Host configuration can replace/add handlers; transport limits alone cannot certify their behavior. Authentication, proxies and runtime retries need separate scrutiny. |

The table's production paths are under `src/Penghou.Baize/` unless prefixed
`Router/`, which means `src/Penghou.Baize.Router/`.

The four audited native streaming implementations are OpenAI-compatible,
Claude Messages, Gemini generateContent and Ollama chat. Each derives from the
shared base and uses the provided HTTP factory. Their bounded baseline probes
each observe one semantic entry, one neutral send, one fake backend send and a
serialized output cap of 512. That is a path demonstration, not provider support
or proof that the cap covers every billable output/reasoning unit. No arbitrary
provider SDK/constructor, custom decorator or external module is certified.

## Path classification

SUPPORTED means the specified enforcement point is demonstrated, not that a
future runner guard, journal or budget ledger already exists.

| Execution path / claim | Classification | Evidence / restriction |
| --- | --- | --- |
| Routed semantic denial before credentials/native construction | **SUPPORTED** | Probe `semantic-denial-before-secrets`: one denied entry, zero secret reads and backend sends. Existing RoutedModelTransport tests agree. |
| Pure metadata lookup for offline planning | **SUPPORTED** | `metadata-resolution-is-credential-free`: one endpoint resolved, zero semantic entries, credentials, constructions or sends. Register denying secret/HTTP services defensively and use only allowlisted modules. |
| Existing endpoint validation as an offline check | **GAP** | Probe validation succeeds and resolves one secret/constructs one native client while semantic entries remain zero. Do not call it from `inventory validate`/`plan`. |
| Single routed built-in streaming chat attempt | **SUPPORTED WITH LIMITATIONS** | One frozen endpoint, no fallback, MaximumAttempts=1, private DI, no extra decorators and an owned admission scope. Re-enumeration crosses the semantic gate again. |
| Multiple endpoints with MaximumAttempts=1 | **SUPPORTED WITH LIMITATIONS** | Probe produces two semantic entries, two secret reads and two sends on first-endpoint 503. Excluded from initial qualification composition. |
| Built-in direct clients and collected completion | **SUPPORTED WITH LIMITATIONS** | Ports exist, but direct constructors receive credentials before the factory runs, and collected completion is not native completion evidence. Use for fixtures; do not add a second v1 execution path. |
| Neutral HTTP send admission/count | **SUPPORTED** | The public async port can deny before its backend. It counts neutral sends, not physical wire attempts. |
| Last application handler admission / request identity and destination checks | **SUPPORTED WITH LIMITATIONS** | Retry/substitution probes demonstrate the point before the fake terminal backend. An exact, isolated handler chain is required; check URI/method/authority/headers and scope. |
| Every HttpClientHandler/SocketsHttpHandler wire attempt | **GAP** | Runtime-internal retries are below both application points. No demonstrated before-each-wire-attempt hook or universal safe multiplicity bound here. Physical attempts remain unknown. |
| Default redirect/cookie behavior | **SUPPORTED WITH LIMITATIONS** | Source defaults disable them. A custom backend/handler can bypass that assumption; pin construction and reject unreviewed chains. A neutral MaximumRedirects=0 is not attestation of the supplied handler. |
| Immutable model/endpoint/protocol subject | **SUPPORTED WITH LIMITATIONS** | Existing target checking + frozen monitor/plan + final HTTP binding can enforce it. Default reload changes the wire model; protocol/revision provenance is not supplied by ModelTarget. Unknown revisions remain unknown. |
| Request body admission and decoded response delivery caps | **SUPPORTED WITH LIMITATIONS** | Existing finite snapshots/body reads reject overflow. Serialization, read overshoot and buffering precede some checks; these are not exact wire-byte or preallocation bounds. |
| Hard parser/event preallocation or network-byte ceilings | **GAP** | No demonstrated port before every parser allocation or backend wire read. Restrict eligibility when that stronger resource guarantee is required. |
| Account concurrency and body-lifetime request ownership | **SUPPORTED WITH LIMITATIONS** | Owned response/body interfaces permit a lease wrapper; existing identity/disposal tests pass. Account-level admission/leases still have to be implemented in Stage 4. |
| Conservative local token/cost reservation | **SUPPORTED WITH LIMITATIONS** | Async gates and nullable/decimal usage carriers suffice; verified units/rates and the Stage 2 ledger are required. No default enforcement follows from UsageIntent. |
| Actual provider billing, reasoning inclusion and complete usage | **UNKNOWN** | Provider/revision-dependent external semantics; partial/derived totals cannot prove them. Paid admission is blocked if necessary bounds are unavailable. |
| Caller cancellation and response-body total deadline | **SUPPORTED WITH LIMITATIONS** | Existing cancellation/timeout reaches blocking reads. Deferred secret/client work may outlive cancellation. No built-in stream-idle timer. |
| Cooperative cleanup with five-second observation grace | **SUPPORTED WITH LIMITATIONS** | Disposal ownership exists; disposal may block synchronously. Close admission first, invoke cleanup away from the coordinator, stop waiting at grace expiry and retain incomplete state. No hard worker lifetime claim. |
| Arbitrary custom modules/constructors, generation/batch, uncontrolled background dispatch or full GM transport closure | **UNKNOWN** | Not audited/eligible in this chat-only scope. No extension of BQ-1b to solve their GM milestones. |

### Counterexamples against an outer HTTP count

`neutral-count-misses-handler-retry` uses the actual Baize backend with a fake
retry handler: semantic=1, neutral=1, terminal backend=2. Moving a single-slot
guard below that retry handler yields two guard entries but only one backend
send. An inner handler can also substitute the destination after the neutral
check; a final destination check rejects before the fake backend. These are
deterministic application-level counterexamples, not socket-level experiments.

The pinned [.NET 10.0.12 connection-pool source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs)
contains a retry loop at lines 348–484, including connection-failure, version
fallback and stream-limit retry paths. The connection-failure ceiling of three
does not bound every branch. This establishes available behavior below an
application handler; it does not establish that any particular provider request
was replayed or charged. Do not assume four wire attempts is a universal safe
reservation multiplier. No real profile earns a hard wire-count guarantee from
this audit. Keep it offline-only until a scoped lower-boundary proof/decision
exists; do not replace the HTTP stack to conceal the gap.

## Budgets, ownership and lifecycle feasibility

**Reservations:** both model transport methods can await durable reservation
before forwarding; the HTTP port/last application boundary can await intent and
consume a request allowance. `ModelUsageIntent` is descriptive input, not a
default enforced quota. There is no current qualification ledger. Implement one
local coordinator in Stage 2, not another provider/router accounting system.
The one-final-slot probe releases two participants behind a barrier and admits
one. Concurrency leases must be held until owned response-body disposal, not
released when SendAsync returns headers. The public `IHttpResponseBody` wrapper
port supports that design; existing transports do not provide account-level
lease enforcement themselves. Validate request/response IDs and make releases
idempotent. A connection limit is not a provider/account allowance.

**Tokens/cost:** nullable input/output/total/cache/thinking/cost/currency can be
observed through existing model responses. None independently certifies a
complete billable sample, token upper bound or price. A partial Ollama fixture
has prompt=3, output=null, derived total=3. An OpenAI fixture with a wire reasoning
detail exposes no ThinkingTokens through the current mapping. These do not
justify zero unknown output/reasoning or release of the reservation. Claude also
derives a total from nullable components (`ClaudeChatClient.cs`:266–274).
Use provider/revision-specific completeness and overlap rules; do not sum repeated
cumulative usage events or double-charge a reasoning subset. No adapter repair
is required to retain full unknown liability.

V3 qualification diagnostics deliberately retain only selected usage fields
(`QualificationScenariosV3.cs`:318–321), not a full billable usage envelope. Stage
3/5 must observe the existing semantic response usage in parallel with contract
results; do not change accepted assertions or invent a second model invocation.
Missing bounds/rates/allowance/currency/billable reasoning semantics block paid
admission. Decimal money and nullable actuals already fit the public interfaces;
the completeness/source/uncertainty assessment belongs in the minimum runner
evidence. Provider prices, weight revisions and actual external billing remain
external facts, not interface guarantees.

**Bytes:** the legacy adapter's bounded request snapshot rejects a six-byte body
under a two-byte cap before neutral/backend dispatch. The public HTTP limits can
be tightened. But snapshots/serialization occur before a later neutral guard,
and provider parsers may materialize an entire line/chunk before an event guard
sees it. A three-byte response cap can read four bytes before rejecting delivery
and disposing; HttpClient buffering is further outside that decoded-byte count.
Do not report these as hard network-byte or preallocation limits. Clamp delegated
reads to remaining+one overflow sentinel, cap existing backend headers before
parse where supported, and explicitly distinguish delivered/body-read/event/
allocation/wire quantities. Profiles needing an unavailable preallocation bound
remain ineligible; no unbounded raw capture is a workaround.

**Lifecycle:** direct and neutral response bodies own disposal and propagate
tokens. Total timeout reaches body reads; a separate read token can implement
an idle deadline. The probe cancels that token manually, proving the port rather
than pretending an idle supervisor already exists. Account for positive byte
progress and retain an absolute total deadline; empty reads/heartbeats cannot
extend it forever. Deferred creation cancellation is a waiter-only property:
the fake secret receives no cancellable token and a native client is constructed
after the caller stops. A runner-owned secret decorator must consult the active
lease before and after asynchronous resolution, even if its argument token is
default. Late/background work must recheck a revocable lease at HTTP admission.

The captured-background-scope probe demonstrates that a shared closed lease
can deny a captured asynchronous context after the semantic enumeration ends.
All four native probes observe the fixture's ambient lease at the HTTP port
even though HttpRequestContext is null. This is a feasible runner bridge, not
a global current-model variable or authority conferred by caller IDs. Concurrent
scope isolation and all late-callback cases remain required implementation tests.

`DisposeAsync` on the existing owned body calls synchronous stream.Dispose
(`BaizeHttpTransport.cs`:117–121). The blocking-dispose fixture proves that merely
putting WaitAsync after an inline call cannot provide a bound. Schedule cleanup
before awaiting it, use the accepted five-second grace, record incomplete cleanup
and unknown spend, and never reopen admission. This is cooperative containment;
no process supervisor or hard termination guarantee is introduced.

Freeze effective timeout ceilings: `BaizeHttp` defaults to 100 seconds while the
plan proposes a 120-second attempt. Use the existing explicit endpoint timeout
composition or report the tighter actual ceiling. A requested timeout alone is
not proof of the effective lifetime. Also, a real runner must own lease
revocation outside the enumerator's finally block: hung inner disposal must not
prevent admission closure. The tiny probe fixtures are not a production cleanup
or concurrency supervisor.

## Gaps, smallest corrections and ownership

These are implementation prerequisites/restrictions, not production edits made
by Stage 0. Future runner components below live under
`eng/Penghou.Baize.Qualification/`; their names are responsibilities, not new APIs.

| ID / affected existing boundary | Smallest correction | Required tests | Owner / gate |
| --- | --- | --- | --- |
| S0-G1 — no before-each-runtime-wire-attempt gate (`BaizeHttpTransport`:37; runtime pool) | Persist separate neutral/backend/wire coverage. Keep real backend eligibility false for hard-wire profiles; use the existing HTTP backend with a pinned chain and a last application guard for observable sends. A genuinely lower wire gate/bound, if later demanded, needs a separate scoped decision; no second stack. | Hidden retry above/below guard, redirect/auth/proxy/version settings, unobserved replay => blocked live eligibility, final-slot contention and disposal lease. | Stage 1 eligibility field; Stage 4 path audit. No production correction needed before offline Stage 1. |
| S0-G2 — endpoint validation constructs clients outside semantic gate (`ReloadingLlmRoutingState`:169–181,306) | Planner uses metadata resolution under denying secret/HTTP services, never runtime endpoint validation. Allowlist modules and create no native clients. | Pure resolution reads zero secrets/sends; endpoint validation negative; all-excluded/unknown selectors cannot pass. | Stage 1. |
| S0-G3 — reloadable, caller-owned configuration (`ServiceCollectionExtensions`:259–269; `LlmEndpointOptions.Settings`) | Deep-copy resolved data; private fixed options monitor already supported; freeze protocol/dialect/defaults and verify model target plus final HTTP destination. Restrict to one endpoint and no decorators/fallback. | Model/dialect/profile/source mutation after plan, reload drift and same-ID/different-destination reject; unknown protocol revision stays unknown. | Stage 1 binding, Stage 3 composition, Stage 4 final checks. |
| S0-G4 — deferred work outlives cancelled waiter (`DeferredEndpointClients`:107–134; secret call at extensions:449) | Revocable attempt lease + secret decorator rechecking before/after resolution, then late HTTP denial. Fresh per-trial DI; do not cache permission with credentials. | Secret resolves after cancellation => no key released to late construction; late background send denied; concurrent scope isolation; disposal grace expiry. | Stages 3/5. No required public signature change. |
| S0-G5 — partial/derived usage is not billable completeness (`OllamaChatClient`:414–430, `OpenAiChatClient`:153–163, V3 diagnostics) | Independent safe semantic usage observer, provider-bound completeness/overlap policy and full worst-case reservation whenever unknown. Do not change contracts or treat total/non-null usage as settlement proof. | Partial input/output, missing reasoning/currency, cumulative duplicates, cancelled/over-bound usage and settlement replay retain liabilities. | Stages 2/5. Provider mapping improvements, if desired, separate adapter work. |
| S0-G6 — byte/allocation and idle/cleanup claims exceed current checks (`BaizeHttpTransport`:39,105–121,162–192,256–263) | Tighten existing limits, wrap owned body for accounting/read tokens, distinguish byte units and allocation exposure, and schedule cooperative cleanup away from coordinator. Restrict paths needing an unavailable stronger bound. | Oversized line/header/body/event, overflow sentinel, stalled read, synchronous blocking disposal, token-source races; no false clean/remote-stopped status. | Stages 4/5. Strong process/preallocation enforcement is separate work, not a hidden BQ-1b promise. |
| S0-G7 — no authoritative durable qualification store | Implement the already planned local manifest/reservation/intent/result/settlement journal, exact hashes, exclusive writer and safe typed diagnostics. Await durability before consequential calls. Do not use Activity or router snapshots as durable intent. | Crash points, torn tail preservation, corrupt prefix refusal, double writer, evidence exhaustion and I/O failure => no further admission. | Stage 2, integration Stage 6; minimum envelope is not deferred to BQ-2. |

No gap requires broad routing redesign. Real wire-attempt eligibility and actual
provider economic bounds are intentionally unresolved facts represented by
explicit restrictions, not an open-ended investigation mandate.

## Verification and evidence binding

The [standalone probes](reviews/bq1b-stage0-probes/) are non-packable, outside
the solutions and reference existing projects. The friend assembly name permits
calling the accepted internal baseline contract; no friend/public API was changed.
They include tiny non-durable guard/counter fixtures, not a runner or ledger.
The fake provider wrapper exists only to observe construction/nested factories.
Runtime: .NET 10.0.12; SDK: 10.0.401; Windows. Raw probe stdout is safe JSONL;
stderr reports harness completion. Each invariant throws on mismatch and every
record explicitly leaves physicalWireAttempts null.

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1b_stage0_no_environment_file__'
dotnet restore docs/reviews/bq1b-stage0-probes/BoundaryProbes.csproj --configfile docs/reviews/bq1b-stage0-probes/NuGet.Config -p:NuGetAudit=false -m:1 --verbosity quiet
dotnet build docs/reviews/bq1b-stage0-probes/BoundaryProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet docs/reviews/bq1b-stage0-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll
dotnet tests/Penghou.Baize.Tests/bin/Release/net10.0/Penghou.Baize.Tests.dll -noLogo -class '*ModelTransportQualificationTests' -class '*HttpTransportLifecycleTests' -class '*HttpTransportFailureTests' -class '*BaizeHttpTransportAdoptionTests'
dotnet tests/Penghou.Baize.Router.Tests/bin/Release/net10.0/Penghou.Baize.Router.Tests.dll -noLogo -class '*RoutedModelTransportTests'
dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo
```

Results: **26/26 probe invariants**, exact case/order-sensitive output replay;
**25 existing core boundary tests**, **7 routed boundary tests**, and the
unchanged **183 qualification tests** passed, with zero failures/skips.
Probe build: zero warnings/errors. Existing Release test binaries were reused
from the accepted mechanical build; their source is unchanged at this base.
No new full-solution, coverage-floor or online vulnerability audit is claimed.
Restore used cached dependencies and cleared package sources; the command-local
audit option does not alter repository policy. An initial fixture build needed
the explicit capabilities constructor arguments; that was fixed before execution.

The probe manifest binds base, project/source and result hashes (UTF-8 with LF
normalization); results are audit observations, not BQ-1b run envelopes or model
qualification claims. Production, accepted contracts and historical probe blobs
must remain identical to the starting commit. Scope/links/whitespace checks and
the final delivery commit complete the record.

**Stage 1 may begin after explicit implementation authorization**, using the
decisions/restrictions above. Stage 0 makes no code/merge/live/publication
authorization and initiates no further BQ-1a review.
