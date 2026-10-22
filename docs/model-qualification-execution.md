# Local qualification execution contract

Status: Proposed v1; commands below are requirements, not runnable features.
See [plan](model-qualification-plan.md) for projects and phased delivery and
[evidence](model-qualification-evidence.md) for authoritative records.

## CLI and configuration

Proposed invocation: `dotnet run --project eng/Penghou.Baize.Qualification -- <command>`.
Prefer explicit command switches over ambient state. Secrets resolve only from
named environment references through existing `ISecretProvider` composition.
Never accept key values on the command line or dump environment variables.
Do not invoke `LiveEnvironment.Load()` in the runner. `--env-file` is not a v1
feature; the operator supplies environment outside the process.

| Command | Required behavior |
| --- | --- |
| `inventory validate --inventory FILE` | Offline validation of IDs, adapter allowlist, URI/profile/settings, exclusions and capability shapes. No endpoint probing or secret resolution. |
| `plan --inventory FILE --policy FILE --models IDS --contracts IDS --out DIR` | Offline frozen resolved plan, hashes, applicability, request/token/cost upper bounds and limitations. Empty/unknown selectors fail rather than run everything. `--all-reviewed` is explicit. |
| `run --plan FILE --output DIR --allow-live` | Validate immutable plan and credentials/admission, acquire exclusive lock, commit manifest then execute. Without explicit live switch it refuses network; no implicit execution from plan. |
| `resume --run DIR --allow-live` | Verify hashes/build/config and replay journal; execute only unsent work under original residual envelope. Never replenish budgets automatically. |
| `report --run DIR` | Offline partial/full JSON and Markdown, including unresolved dispatches and remaining limits. Usable after abrupt termination. |
| `project --runs DIR --policy FILE --as-of UTC --out DIR` | BQ-2 deterministic capability projection; no configuration write-back. |
| `compare --before FILE --after FILE --out DIR` | BQ-4 classify drift, cohort changes, new quirks, missing coverage and blockers. |
| `inventory discover --profile ID --output FILE --allow-network` | Later optional bounded metadata-only GET; never modifies curated inventory or invokes inference. Uses a separate explicit request allowance and secret admission. |
| `export` / `import` | BQ-5 immutable shard bundles; reject collisions/traversal/hash mismatch. No remote submission. |

Exit codes: 0 = command completed and all selected requirements satisfied;
2 = invalid configuration/plan or unsupported schema/build; 3 = conclusive
contract failure/unstable result; 4 = blocked, incomplete, insufficient budget
or unresolved dispatch; 5 = evidence/internal failure; 130 = operator cancellation.
Precedence for a mixed run: internal corruption > cancellation > invalid plan >
incomplete/block > contract failure > success. JSON summary always lists every
condition, so a numeric exit cannot hide failures. Offline validation success is
not a live success. Empty selection or all-excluded execution is never a green
qualification run. stdout offers versioned JSONL progress; stderr is sanitized
human progress. Durable local evidence is authoritative if a pipe breaks.

Illustrative inventory, deliberately not authorized for live execution:

```json
{
  "kind": "qualification-inventory",
  "schemaVersion": "1.0",
  "entries": [
    {
      "id": "nvidia-llama31-8b",
      "service": "nvidia-hosted",
      "endpointProfile": "nvidia-build-evaluation",
      "accountScopeRef": "operator-evaluation-account",
      "protocol": "openai-chat-completions",
      "apiVersion": "v1",
      "family": "llama-3.1",
      "revision": null,
      "mutableAlias": true,
      "endpoint": {
        "Id": "nvidia-build-llama31-8b",
        "Provider": "OpenAi",
        "ProviderModel": "meta/llama-3.1-8b-instruct",
        "BaseUrl": "https://integrate.api.nvidia.com/v1",
        "ApiKeySecretName": "NVIDIA_API_KEY",
        "Settings": { "Dialect": "Standard" },
        "RequestTimeout": "00:02:00",
        "Capabilities": { "NativeToolCalling": false }
      },
      "advertised": [],
      "profiles": ["text-baseline"],
      "exclusions": [],
      "economicsRef": null,
      "allowanceRef": null
    }
  ]
}
```

`endpoint` follows existing `LlmEndpointOptions`, including named profile
resolution and settings. Record resolved defaults separately; null fields inherit
only through the current Baize configuration semantics. `advertised` entries
carry facet/value, source URL/date and limitations. Exclusions specify contract,
reason, source, owner and review expiry. Unknown settings or arbitrary modules
are rejected: v1 only permits the existing built-in provider assemblies reviewed
in the manifest. Custom providers require demonstrated dispatch coverage first.

URI validation requires HTTPS, approved host/port/base path, no userinfo/query/
fragment; local HTTP is a separate explicit loopback-only development profile.
No endpoint/profile switch after planning. HTTP destination and method allowlists
are independently checked at dispatch, redirects disabled; remote fixture URL
fetching is excluded from BQ-1. Reasoning/media controls not representable through
the configured adapter are `Not Tested` coverage gaps, not free-form JSON patches.

## Policy and bounds

Policy v1 is a reviewed JSON document with explicit positive limits; no `0`
meaning unlimited. Required fields: `maximumHttpRequests`, `maximumModelCalls`,
`maximumConcurrentModels`, `maximumConcurrentRequestsPerAccount`,
`minimumRequestSpacing`, `maximumInputTokens`, `maximumOutputTokens`,
`maximumTotalTokens`, `maximumEstimatedCost` with currency/economics source,
`maximumDuration`, `attemptTimeout`, `streamIdleTimeout`,
`maximumRequestBytes`, `maximumResponseBytes`, `maximumEventBytes`,
`maximumEvidenceBytes`, `repeatCount`, `maximumAvailabilityRetries`,
`endpointAllowlist`, and capture policy. Account/provider ceilings further narrow
these limits. Every request, including discovery, validation probes, continuations
and retries, consumes a request allowance. Offline structural validation is free
of provider requests; do not assume endpoint validation is free without inspection.

Proposed local baseline limits for review: concurrency 1 globally/per account,
10-minute run deadline, 120-second attempt deadline, 30-second stream idle timeout,
1 MiB request, 4 MiB response, 64 KiB event, 16 MiB safe evidence, no automatic
retry, three predeclared trials. Numeric request/token/cost ceilings are computed
from the selected plan and explicitly accepted by the operator. The new defaults
are intentionally independent of the legacy five-minute harness timeout. Changes
to timeouts or output caps create a different configuration identity.

Example baseline-only envelope: three models × three trials × one dispatch =
nine semantic/HTTP requests, before any separately budgeted discovery. With
per-call output cap 512, output reservation is 4,608 tokens **only if** provider
billing semantics confirm that cap includes all charged output/reasoning. Input
reservation and prices still need independent bounds. A reasoning model that
needs a larger cap must receive a separately reviewed plan; the runner does not
silently grow it after a truncated result. Sequential and parallel tool scenarios
reserve three and two dispatches per trial respectively, with entire conversation
growth and tool result replay included in input bounds.

### Reservation algorithm and fail-closed accounting

1. Resolve owner-approved allowance and dated economics for each exact endpoint.
   For paid access require currency, input/output/reasoning/media price ceilings,
   validity window and independently bounded units. A confirmed no-charge trial
   can use explicit `verifiedTrialAllowance` with source/date/expiry and request/
   token limits; null economics never means free. No account balance API is assumed.
2. Each scenario descriptor provides the maximum number and shape of calls.
   Token counts need an exact tokenizer or a defensible provider-specific upper
   bound including chat template, schemas, conversation growth and reasoning.
   A heuristic characters/4 estimate is insufficient for a hard cap. A documented
   enforced maximum context bound can be used conservatively if its billing
   semantics are known. If no safe bound exists, that live case is blocked.
3. Before each semantic dispatch, under a process-local lock, durably reserve
   maximum input/output/total and estimated cost. Cost ceiling = sum of bounded
   billable units × reviewed upper-bound rates (including fixed/request charges).
   Reserve worst-case retries if selected. No reservation can exceed the remaining
   global, account, model or case envelope. Reject before credential resolution.
4. Before every physical HTTP send, atomically consume a request slot and enforce
   destination, concurrency, deadline and byte bounds. Semantic and HTTP IDs are
   correlated but their counters are distinct. Hold a streaming concurrency slot
   until body disposal, not just headers. Unexpected extra HTTP dispatch fails
   closed; do not double-charge tokens for a single semantic call's known HTTP
   plumbing. An unaccounted inference POST is denied.
5. On completion settle usage only from complete trustworthy provider usage or
   proven no-dispatch. Otherwise retain the full conservative reservation as
   `spentUnknown`; report actual as null. Treat missing usage, cancelled streams,
   dropped connections and crash-after-intent as potentially fully charged.
   Unexpected usage above a claimed bound halts the run and marks the economics
   policy invalid; a local estimate cannot undo an external charge.
6. A journal write/fsync failure, unknown reservation state, expired price/allowance,
   or inability to establish remaining safe allowance prevents new dispatches.
   Never reset budget on resume. Top-up requires an explicitly authorized new
   plan/run linked to the prior run, carrying unresolved liabilities forward.

The promise is a hard local **admission** cap under verified bounds, not a provider
billing guarantee. Unknown economics block paid inference. Shared-account external
usage requires a host budget allocator or an operator-reserved dedicated allowance;
the runner does not discover or control other programs' spending. In-process guards
cannot contain arbitrary untrusted code; v1 accepts only reviewed built-in scenarios.

## Retries, execution and cancellation

Use existing Baize composition with one endpoint and no fallback; disable router
same-route retry with `MaximumAttempts=1`. Audit provider and HttpClient handlers
for hidden retries before supporting a path. BQ-1 defaults availability retries to
zero. Later policy may allow one retry on explicit 429/503/no-output availability
responses with `Retry-After`, bounded backoff/jitter and preserved attempt identity.
Never wait less than server guidance: if it exceeds the run deadline, stop Blocked.
Retries consume new reservations and do not replace failed first trials. Never
retry invalid model output automatically, nor an ambiguous submitted POST by default.

Each repetition has a fresh conversation and DI scope. Multi-turn continuations
stay within one trial, retain assistant parts/tool IDs and use local deterministic
tools only. Never execute arbitrary generated tools. Model output cannot request
additional scenarios, network access, files or budget. No repair/retry changes
an assertion; separate structured-output repair profiles expose their provenance.

Cancellation links operator, run, case and HTTP tokens and stops new admission
first. Dispose enumerators/responses and DI scope, write terminal/partial evidence
and unresolved usage. Use a bounded five-second cleanup grace before marking
unfinished cleanup; do not claim remote cancellation. A future workflow process
can terminate a hung runner after its deadline plus grace; the next resume treats
unfinished intents as unknown spend. A cancellation contract succeeds only when
its expected cancellation and disposal checks occur; an unrelated cancelled run
does not earn that capability.

## Durable local run and resume

Proposed layout:

```text
artifacts/qualification/<run-id>/
  manifest.json
  plan.json
  events-0001.jsonl
  artifacts/<sha256>.<extension>
  reports/summary.json
  reports/report.md
  run.lock
```

Acquire an OS-exclusive lock; refuse a second writer. Create manifest/plan once
using create-new temporary file, flush-to-disk and atomic rename on the same local
filesystem. Each journal line is bounded, sequenced and flushed before any
consequential next action. Never start a send unless ReservationCommitted and
DispatchIntent have durably completed. Append terminal result before marking a
case complete. Side artifacts are atomic/hash-checked before references are committed.
No network share/distributed locking guarantee in v1; detect unsupported storage
or document a local-filesystem prerequisite.

On resume verify runner/build/contract/fixture/config hashes and endpoint binding.
A changed plan, adapter or assertion requires a new linked run. Rebuild projections
from valid committed events; mutable report files are disposable. Do not resolve
secrets until eligibility and remaining budget checks succeed.

| Last durable state | Recovery behavior |
| --- | --- |
| Planned, no reservation/intent | Execute normally within remaining envelope |
| Reserved, no intent | No HTTP could have been sent by contract; append release/recovery event before re-planning |
| Dispatch intent, no terminal result | Mark interrupted/inconclusive and retain full unknown-spend reservation. Do not automatically resend |
| Terminal result, settlement absent | Recover idempotent settlement from result, retaining unknown reserve if usage incomplete; never rerun |
| Terminal result and settlement | Skip completed trial, including failures; no retry-until-pass |
| Torn final line | Preserve segment bytes unchanged; close it with a recovery record in a new segment referencing hash and last valid prefix. Account for any valid prior intent as unknown; never truncate history |
| Hash/sequence corruption in committed prefix, conflicting IDs or ambiguous lock ownership | Refuse resume, offline partial report only, human investigation |

Exactly-once remote execution is not promised. To repeat an interrupted trial,
the user authorizes a **new linked run** with a new attempt ID and allowance that
covers the previous unknown liability; original incomplete evidence remains.
For multi-turn cases restart the whole trial only under that new plan, never
fabricate assistant continuation state from partial logs. The design therefore
does not retain raw conversations merely to support resume.

## Required offline acceptance tests for BQ-1b

Tests must observe boundaries, not merely serialize implementation fields:

- Two inventory models use isolated endpoints/configuration and exactly the
  planned calls; unknown selector and all-excluded plan do not pass silently.
- Invalid configuration and plan-only mode resolve no secrets and make no network
  calls; host/budget denial precedes credential access and provider dispatch.
- Concurrent reservation at the final slot permits one request; exhausted token,
  request, cost or evidence allowance permits zero further sends. Hidden retries,
  destination substitution and redirect attempts are rejected.
- Missing/partial usage, cancellation and network response loss retain reserve;
  lower trustworthy actual usage settles once; retry keeps the first failure.
- Inject process/storage failure before/after each reservation/intent/result/
  settlement boundary; resume neither loses liabilities nor duplicates completed
  sends. Test torn tail preservation, corrupt prefix refusal and writer exclusion.
- Delayed stream enforces timeout/idle deadline and disposes body; cancellation
  stops fan-out and produces a usable partial report.
- Sentinel credentials, prompt text, private URLs and tool payloads never appear
  in default events, stdout/stderr, exceptions or artifacts. Opt-in capture is
  bounded, sanitized and expires with tombstones.

No live acceptance is needed to merge offline BQ-1 tooling. Before its first
authorized live use, independently review the dispatch-path audit, bound sources,
secret ordering and immutable plan. Existing deterministic package gates remain.
