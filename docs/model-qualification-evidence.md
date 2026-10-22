# Capability and evidence schema specification

Status: Proposed v1, design only. [Architecture](model-qualification-plan.md) and
[execution](model-qualification-execution.md) define producers. This specification
defines records; no schema implementation or catalogue is delivered yet.

## Record family and identity

Use UTF-8 JSON, RFC 3339 UTC timestamps, UUID run IDs, ordinal case-sensitive
provider model identifiers, integer counters, and decimal-string currency
amounts with ISO currency codes. Never use floating-point money. Nullable means
unknown, never zero. Every persisted document has `kind` and `schemaVersion`
(`1.0`). Readers reject unknown major versions and unknown authoritative enum
values; they preserve/ignore unknown optional fields within a supported major.
New required fields or changed semantics require a major version. Schema upgrades
emit new derived documents linked to originals; never rewrite source evidence.

Hash exact stored UTF-8 bytes with SHA-256, lowercase hex and `sha256:` prefix.
Configuration identity is produced by one versioned canonical writer: ordinal
property sorting, normalized decimal strings, explicit defaults, no insignificant
whitespace, ordered arrays preserved (set-like arrays sorted at resolution).
Publish writer conformance vectors in BQ-2. No raw secrets or secret hashes enter
the canonical form. Never hash sensitive low-entropy prompt text as a substitute
for protecting it; default fixtures must be repository-owned synthetic data.

The qualification subject key includes:

- Hosting service (`nvidia-hosted` versus `deepseek-native`), adapter provider key
  and version, host-selected endpoint profile ID and its configuration digest.
- Normalized HTTPS origin/port/base path, API protocol and version, explicit
  dialect/settings and account-scope reference (opaque, nonsecret, not a credential).
- Requested provider model ID, family/revision if known, response model/revision
  observations and whether the request name is an alias.
- Effective `LlmEndpointCapabilities`, request configuration digest (reasoning,
  sampling, token caps, tool/schema identities, response mode, media transport),
  contract ID/version, fixture digest, Baize commit/build and adapter versions.

Do not collapse different endpoints, regions, tenants, native versus compatible
APIs, capability overrides, schema modes or reasoning settings. With unknown
revision, comparisons are explicitly alias observations across a dated window;
they cannot establish identical weights. A revision changing within a run splits
results into distinct subject cohorts and sets drift; it cannot produce a single
Verified cohort. Build changes are separate subjects; comparison reports may
link them without pooling their claims.

## Minimum durable records

| Record | Required content and invariants |
| --- | --- |
| `run-manifest` | Run ID; creation time; base/repository commit, clean/dirty state and optional reviewed patch digest; package/assembly versions and resolved dependency digest; runner build/hash, .NET/OS; contract/fixture versions; inventory, policy and plan hashes; endpoint bindings; safe resolved request parameters; selected/excluded case list; budget envelope; credential *references*; source citations; supported evidence/capture policy. Clean tree required for promotable runs. |
| `case-plan` (inside manifest or hashed file) | Stable case ID = digest of subject/configuration/contract/fixture; ordinal repetition slots; expected applicability, named assertions, maximum semantic/HTTP calls and token reservation by step; exclusion reason/owner/review expiry. One baseline dispatch may satisfy several separately reported facets. |
| `run-event` | Run ID; monotonic sequence; event ID; UTC time; type; previous-event hash; payload; event hash computed over canonical record excluding its own hash. First event binds manifest hash. Types: RunStarted, AttemptPlanned, ReservationCommitted, DispatchIntent, DispatchObserved, AssertionObserved, AttemptFinished, UsageSettled, ResumeStarted, CorrectionAppended, RunFinished. |
| `attempt-result` (AttemptFinished payload) | Case ID, trial slot, attempt ID, retry-of, start/end/elapsed, phase reached, semantic invocation and HTTP request IDs/counts; per-assertion pass/fail/not-observable; outcome and failure taxonomy; terminal reason, response mode, integrity diagnostics; original/repair validation summaries; usage/cost with source and uncertainty; rate-limit/timeout/retry/cancellation data; quirks/adaptations; artifact references; limitations. No raw exception dump by default. |
| `artifact-reference` | Relative non-traversing path or approved opaque store ID, content hash, media type, byte length, producer, sensitivity class, retention/expiry, sanitization version, completeness/truncation. Public-safe fixture artifacts and diagnostic artifacts are separate. |
| `correction` | Target event/record hashes, author/reviewer identity, reason, corrected classification or metadata and supporting evidence. Append-only; original remains readable. Assertion fact changes require a new run, not an editorial correction. |
| `run-summary` | Manifest hash and last committed event hash/sequence, complete/partial/cancelled/budget-blocked/error disposition, counts by case and classification, unresolved spend/reservations, omitted cases, limitations. Derived and replaceable; never source of truth. |
| `catalogue-projection` | Schema/projector/policy versions; explicit as-of time; sorted input manifest/tip/correction hashes; subject/facet states, counters/window, freshness/conflicts/blockers, evidence refs and unresolved gaps. Reproducing with identical inputs/as-of yields identical bytes. |
| `approval-snapshot` | Immutable projection hash, selected capabilities mapped to existing Baize profile fields, exclusions, reviewer/approval date/expiry, scope and target config hash, rationale and supersedes/revokes references. Separate host artifact; no runner command silently installs it. |
| `review-annotation` | Referenced evidence hashes, author type (human/AI), model if AI, creation time and explanation/proposal. Untrusted commentary; projector never reads it as assertion or approval. |

For BQ-1 the manifest embeds descriptor and case plans; JSONL events contain
attempt results. No separate file per field or generalized event-bus dependency.
BQ-2 publishes JSON Schema files for the envelopes under `docs/qualification/`
and tests fixtures against them. Artifact and event byte limits are explicit in
the execution specification.

Illustrative projection entry (not live evidence):

```json
{
  "kind": "capability-observation",
  "schemaVersion": "1.0",
  "subjectRef": "example-subject",
  "contract": "tools.single@2",
  "facet": "NativeToolCalling",
  "state": "Not Tested",
  "advertised": { "value": null, "sourceRefs": [] },
  "configured": { "value": true, "purpose": "qualification-probe" },
  "observed": { "passed": 0, "failed": 0, "blocked": 0 },
  "approvedProfileRef": null,
  "freshness": "unknown",
  "evidenceRefs": [],
  "limitations": ["Illustrative record; no provider was called."]
}
```

Advertised data is source-attributed documentation, configured data is what Baize
was told, observed data is what checks established, approved data is a reviewed
host profile. Configuring a probe flag to true is not a support assertion. A
locally rejected request is evidence about that configured Baize composition,
not proof that the remote model lacks the feature.

## Attempt outcomes, state and attribution

Attempts use `Passed`, `Failed`, `Blocked`, `Cancelled`, `Inconclusive` or
`NotRun`. Applicability is a separate `Applicable`, `Excluded` or `NotApplicable`
field with rationale. Final catalogue states are:

| State | Rule for an exact subject and contract facet |
| --- | --- |
| Verified | All mandatory assertions are observed and pass for all predeclared qualifying trials, sufficient policy sample, no unresolved conflicting evidence in the epoch; mode and limits remain attached |
| Failed | Reproducible contract failure: deterministic replay proves the defect, or two independently dispatched identical-condition attempts establish the same failure; says nothing by itself about fault owner |
| Unsupported | Explicit authoritative scope-specific unavailability/rejection is established, with valid request/configuration and attribution. A single unambiguous provider unsupported-feature response can establish this; generic 400/404 cannot |
| Unstable | Mixed conclusive pass/fail, unresolved contradiction, or measured success below declared stability criteria; a solitary stochastic failure is Unstable with `insufficientEvidence=true`, never Unsupported |
| Blocked | Required qualification could not be completed because access/quota/availability/budget/infrastructure prevented testing; include partial passes and prior observation separately |
| Not Tested | No qualifying evidence, intentional exclusion/not-applicable, or only insufficient positive samples. Include `partial=true` and observed passes; never silently round up to Verified |

Record the failure *phenomenon* and independently the attribution/confidence:
`baize-protocol`, `provider-incompatibility`, `model-output-invalid`,
`rate-limited`, `quota-exhausted`, `authentication`, `authorization`,
`provider-unavailable`, `timeout`, `caller-cancelled`, `invalid-configuration`,
`host-authority-denied`, `budget-denied`, `evidence-io`, `inconclusive`.
Retain safe HTTP status/provider error code, stage, check IDs and retry guidance.
HTTP 401/403 does not prove Unsupported; a 404 could be endpoint style or access.
Unknown 5xx cause is provider-unavailable, not an adapter defect. A caller token
and deadline token are recorded separately; ambiguous cancellation stays inconclusive.

Attribution has `basis` = deterministic-fixture / sanitized-protocol / explicit-
provider-code / observed-normalized-output / unresolved, and references. The
runner can classify a failed field assertion as invalid model output only if
protocol decoding is trusted for that shape; otherwise mark unresolved. A replay
of a preserved sanitized correct provider response that Baize corrupts confirms
an adapter defect. Human correction can refine attribution without changing the
original failed check. LLM explanations cannot refine it authoritatively.

## Projection and reliability policy v1 (proposed)

1. Verify schema, manifests, event chains, artifact hashes and committed terminal
   records; reject conflicting duplicate IDs. Never ingest a torn JSONL tail as
   successful evidence. Order by run time/sequence with deterministic ID tie-breaks.
2. Group by exact subject and facet. Include all predeclared eligible trial slots,
   retries, blockers and exclusions. Apply only reviewed provenance-preserving
   corrections. Do not reinterpret an old contract under newer assertions.
3. Default qualification cohort is **three first trials** per selected contract.
   No early stop on success; stopping for safety leaves a partial cohort. One
   pass is a valid observation, but not a v1 current qualification. Mixed pass/fail
   is Unstable, even if retries later pass. Reproducible failures dominate partial
   positives as conflicts; confirmed unsupported plus a conclusive pass is a
   conflict requiring review, not last-write-wins.
4. If no conclusive failure exists, blocked required slots yield Blocked;
   unexecuted/cancelled slots yield incomplete/Not Tested. All three conclusive
   passes with no conflicts yield Verified. Missing optional provider usage is
   a limitation, not a failed content check, provided budget safety remains proven.
5. Freeze an eligibility window/epoch in the policy. Default freshness is 30 days
   for pinned revisions and 7 days for mutable/unknown aliases. These are reviewable
   operational defaults, not guarantees. Preserve historical Verified while marking
   `freshness=stale` and `promotionEligible=false`. A later outage can coexist with
   prior capability verification and current `availability=blocked`.
6. New adapter/config/contract/revision starts a new cohort. Same-identity recovery
   after a conflict requires reviewed supersession with reason and new complete
   cohort; old failures remain in history and drift reports. Never silently reset
   an epoch because the latest run passed. Contradictions remain visible even when
   older evidence ages out of the current policy window.

Report initial attempts, all physical dispatches, retry recovery, blocked and
inconclusive counts separately. Observed success ratio uses conclusive trials as
denominator and prints excluded counts alongside it; availability ratio uses all
attempted trials. Each multi-turn trial succeeds only if all steps pass. Three
passes do not imply a population success probability or an SLA. Deeper advisory
reliability may predeclare ten trials across at least two sessions; publish raw
counts, latency distribution/sample size, token uncertainty and selection bias.
No automatic confidence percentage from tiny samples; no ranking models by intelligence.

## Reproducibility, history and privacy

Independent reproduction requires access to the exact commit/build, dependency
versions, runner/descriptor versions, synthetic fixture bytes/generators, safe
resolved request settings, inventory binding, plan/policy and credentials with
equivalent authorized endpoint access. Supply an offline `plan` command and the
exact approved run command, OS/SDK and limitations. Re-running cannot guarantee
the same model behavior, hidden revision, service tier or quota. A live result
proves only the recorded conditions at that time.

Retain existing Markdown matrix/log unchanged. A later legacy importer references
their commit/path/row and date with `evidenceClass=legacy-narrative`, unknown
runner/package/fixture fields null, and any conflicting prose attached. It may
display historical Pass/Blocked claims but cannot satisfy v1 Verified/promotable
requirements without sufficient original evidence or requalification.

Default evidence contains check IDs, expected fixture references, safe observed
types/counts, finish/error codes, usage, hashes and bounded diagnostics, not
prompts, model text, tool arguments, reasoning traces, credentials, request
headers, signed URLs or raw HTTP. Public repository fixtures are independently
retrievable by hash. For private fixtures store only opaque access-controlled
references and state that public reproduction is restricted.

Detailed capture is separate opt-in: synthetic/public fixtures only by default,
64 KiB each request/response, 1 MiB per run, 24-hour retention, filesystem access
restricted to operator, explicit sensitivity label. Sanitize before persistence
with header allowlists and structural body redaction; drop query strings, user
info, opaque URLs/handles and unrecognized bodies rather than regex-only masking.
Store capture/truncation/sanitizer metadata. Existing diagnostics redaction is a
component to inspect and test, not proof that arbitrary payloads are safe.
Capture failure cannot manufacture a pass; if needed for attribution mark that
attribution inconclusive. Evidence journal failure stops new dispatches.

Default safe run manifests/events have no automatic deletion; the owner controls
retention and backups. Expiring optional sensitive captures appends a tombstone
with hash/time/reason, leaving original evidence references and reduced
reproducibility visible. Never rewrite attempt history to satisfy retention.
Local append-only files are a program invariant, not tamper-proof storage against
a user with write access. Independent review needs a copied/pinned bundle and
host-controlled permissions; later CI may sign hashes without changing schemas.
