# BQ-1b Stage 0 decisions

Date: 2026-10-25. Source base:
`3986dde792f6af49f4f3a51e27db1a6d721d691f`.
Disposition: **READY WITH RESTRICTIONS** for implementation planning.
Basis: [execution-boundary audit and probes](model-qualification-bq1b-stage0-audit.md).

These concrete recommendations resolve the five Stage 0 choices for the initial
offline milestone. They extend the existing architecture without another router,
HTTP stack, workflow engine, public package or production API. They are not an
authorization to start implementation or live execution. BQ-1a is closed and
its accepted contracts/evidence remain unchanged.

## D1 — Count the boundary actually controlled; restrict transport eligibility

Keep three separate quantities: admitted semantic invocations, neutral HTTP
calls and admitted terminal application/backend sends. Wire attempts remain
nullable/unknown when no exact observation or defensible bound exists. None of
the first three is renamed a proven physical-wire count.

Choose the routed streaming path with one frozen model/endpoint per trial,
MaximumAttempts=1, no named/strategy fallback, extra model decorators or dynamic
provider modules. Gate once at the deferred model factory before credentials;
leave the inner native client's model factory pass-through. Re-enumeration and
any unexpected invocation require a new admission and are denied at cap.

Reuse injected `IHttpTransport` and the existing named HttpClient backend. Pin
and audit the full chain; put a final application guard immediately before the
backend and bind method, URI/origin/path/authority, body/model profile, attempt
lease, deadlines and counters. Disable automatic redirect/cookie/credential/
proxy flows in the allowed profile and disallow application retry middleware.
Those settings do not establish control over runtime-internal replay.

For the initial BQ-1b milestone, only deterministic fake terminal transports
establish all required request-boundary observations. Keep actual network
profiles ineligible whenever the required wire-attempt or resource bound is
unproven. A later exact-backend proof or a narrowly proposed existing-seam
correction can establish eligibility; no runtime fork, replacement HTTP stack
or universal retry multiplier is prescribed by BQ-1b. This is an explicit
restriction, not a deferred permission to weaken request budgets.

## D2 — Reserve bounded units; keep unknown usage/economics unknown

Use one local coordinator and the existing semantic/HTTP wrapper ports. Reserve
the full conservative input/output/total/cost envelope durably before deferred
credentials/provider execution. Input bounds must include templates/schemas;
output/reasoning bounds must follow a reviewed provider/protocol revision and
billable-unit definition. A serialized cap of 512 is only a request setting
until its complete billing bound is established.

Unknown token bounds, rates, currency, allowance, reasoning inclusion or expiry
block paid admission. A confirmed no-charge trial still requires dated finite
request/token allowance and proven dispatch coverage. Actual cost stays null
when absent; decimal money/rate ceilings remain source-bound. No heuristic
tokenizer, synthetic zero cost or inferred provider support is introduced.

Observe complete `ModelUsage` snapshots independently of V3's intentionally
smaller diagnostic result. Settle once only when the provider-specific required
fields, terminal completion and units are trustworthy, or when no-dispatch is
proven. Non-null/derived total alone is insufficient. Do not sum cumulative
snapshots or a reasoning subset twice. Partial usage, cancellation, response
loss or unresolved intent retains full `spentUnknown` liability. Over-bound
usage halts admission and invalidates the bound policy. Unknown liabilities
cannot be released or replenished by resume. A provider mapping improvement
is separate adapter work; it is not required to preserve conservative reserves.

## D3 — Cooperative cleanup, five-second grace, no process-lifetime guarantee

Adopt the user's initial claim: close admission first, signal the distinct
operator/total/attempt/idle cancellations, attempt disposal and terminal evidence,
then stop waiting after five seconds. Record incomplete cleanup and retain
unknown spend. Never claim that remote compute/billing stopped.

Owned DisposeAsync can block synchronously before returning a task. Invoke the
cleanup workload away from the coordinator before awaiting it with a grace
deadline. Revoke the shared lease so captured background work cannot gain new
HTTP admission. Wrap secret resolution to recheck that lease before and after
awaiting, since current deferred creation does not pass the caller token through
to the secret resolver. Preserve original cancellation attribution; ambiguous
OCE stays inconclusive.

A body-read wrapper can apply idle cancellation using the existing read token;
existing total deadlines also cover body reads. Use a monotonic injectable clock,
positive-byte progress and an absolute deadline. Any stronger hard worker/process
lifetime guarantee requires a separate host/worker decision. It is not required
to start the offline BQ-1b stages and is not implicitly promised here.

## D4 — Minimum durable evidence is a BQ-1b prerequisite

Implement the already specified v1 local manifest/frozen plan, sequenced/hash-
linked append-only events, exclusive writer and bounded content-hashed artifacts.
Use create-new/flush-to-disk/same-filesystem atomic replacement under a documented
local filesystem prerequisite. No new database or generalized event bus.

Reservation must be durable before semantic forwarding/credentials. Dispatch
intent with its boundary kind must be durable before each admitted send;
DispatchObserved/result/settlement follows separately. Preserve the distinction
between intent, observed backend send and unknown wire attempts. A crash after
intent is unknown potential spend; no automatic resend. Write result before
marking a case complete; recover settlement idempotently. Preserve torn-tail
bytes and refuse corrupt committed history. Evidence I/O/capacity failure closes
admission rather than dropping failures.

Default records contain safe typed statuses/counters, exact build/config/fixture
hashes, opaque IDs, usage source/completeness/uncertainty and raw assertion facts.
Do not persist provider response text, exception.Message, endpoint validation
error strings, raw logger exception output or HTTP capture by default. The
existing base exception path includes response text. Infrastructure outcomes and
attribution remain separate from observed contract checks. They never establish
Unsupported; BQ-2 owns that later projection, corrections and formal schemas.

## D5 — Offline eligibility is explicit and cannot be overridden by allow-live

Stage 1 plans must carry an explicit execution eligibility assessment with reason
and evidence references for each required boundary. Default real-network
eligibility is false until dispatch/resource/economic/configuration requirements
are demonstrated. Unknown is not a positive eligibility state. An operator's
later `--allow-live` authorizes only an already eligible frozen plan; it cannot
turn a missing enforcement point into a guarantee.

Use allowlisted metadata-only lookup for offline resolution. Never call runtime
endpoint validation from `inventory validate` or `plan`: it constructs clients
and reads credentials outside semantic admission. Fixed options snapshots are
already injectable; keep private copies and a defensive secret/network denial
in planning composition. Stage 1 can therefore begin without a production change.

Failed eligibility is an execution block/coverage gap, not model unsupportedness.
Preserve provider/model/protocol provenance unknowns, individual assertion facts
and the known F1 adapter attribution. Do not weaken/retry/repair contracts to
make execution green. BQ-1b may complete its two-fake-model offline acceptance
with real paths blocked. Live qualification, adapter/GM changes, BQ-2 projection,
BQ-3 quirk implementation and BQ-4 full-fidelity scenarios remain separate gates.
