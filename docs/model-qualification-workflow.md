# Workflow integration, routing promotion and graduation

Status: Proposed. No workflow, automation, CI live job, routing promotion or
release action is activated by this design. [Local execution](model-qualification-execution.md)
works without infrastructure or other Penghou components.

## Deterministic workflow boundary

Future Zhinu/Marang templates can parameterize inventory/plan hash, case IDs,
shard envelope, run/store reference, immutable executable version, deadline and
opaque workflow/activity IDs. The host supplies authorization and credential
bindings separately; an ID is correlation data, not permission.

1. Host approves a frozen plan and account allocation. Inventory discovery, if
   requested, produces a reviewable candidate file before plan approval.
2. Deterministic planner partitions case/trial slots by stable ID. No model agent
   chooses new tests, changes thresholds or expands inventory during execution.
3. Scheduler starts the same runner with an exclusive run directory per shard.
   A local scheduler needs only one process; orchestration is external later.
4. Runner records immutable evidence and durable checkpoints before dispatch.
   Scheduler cancellation is the ordinary CLI cancellation contract.
5. Fan-in verifies manifest/contract/build/policy hashes, shard coverage and budget
   partitions. Deduplicate by attempt/event hash; reject divergent same-ID records
   or overlapping trial ownership. Missing shards stay partial, not green.
6. Projector produces catalogue/report and comparator reports changed facets,
   aliases/revisions, new failures and previously unseen quirk signatures.
7. An investigative agent may receive sanitized read-only evidence. It returns
   a separate annotation and proposed regression fixture/code/document diff.
8. Marang may present findings and approval requests. Human review owns changes
   to assertions, thresholds, adaptations, profiles and release claims.

The future result envelope contains `schemaVersion`, run/plan/shard IDs, manifest
and journal-tip hashes, state/exit code, completed/pending case IDs, budget spent/
unknown/residual, evidence references, limitations and `requiresAction` reasons.
stdout progress is advisory; the filesystem/store journal remains authoritative.
Workflow retries invoke `resume`, not `run` on the same work from scratch.
Each execution needs fresh host authorization; a stored handle is not permission.

For fan-out, allocate disjoint request/token/cost envelopes whose sum is no more
than the approved global allowance. An unavailable shard's unspent *reservation*
is not reallocated until its state is reconciled; unknown spend is retained.
Endpoint/account concurrency and RPM limits also need a host-wide lease/token
allocator or serial execution across shards. Disjoint cost allocations alone do
not solve shared rate limits. BQ-5's local rehearsal uses serial shards; distributed
leases, shared credentials and durable scheduling belong to Zhinu/host integration.

## Authority, storage and investigation

Baize owns execution, normalized capabilities, validation and routing mechanics.
Qualification owns dated compatibility evidence and deterministic projections.
Hongxian may store broader contextual execution experience; it does not turn
qualification into a global performance rank. Marang presents decisions; Zhinu
owns workflow scheduling/checkpoints/retries. Host policy owns permission,
budget, credential custody, retention and final promotion.

Future Hufu adapters can authorize each model/HTTP operation through the existing
neutral seams and bind account/endpoint/context/reservations. The local runner
does not require Hufu or implement an authority vocabulary. Gagamba may isolate
investigators with read-only sanitized evidence, no secrets/network by default,
and proposed output in a separate directory. A trusted runner/broker outside that
domain controls provider calls and evidence writes. Both integrations require
their own qualified contracts; in-process guards and file hashes alone do not
contain an agent with arbitrary filesystem/shell access.

The AI reviewer cannot modify authoritative events, regenerate a favorable
summary while hiding inputs, change fixtures/criteria mid-run, reinterpret a
failure as success, install workarounds or promote profiles. Proposed edits are
reviewed commits with new contract/policy hashes and new runs. Retain failed
evidence. Corrections preserve the original and identify a human reviewer.

## Routing integration and later promotion

Current `ConfiguredLlmRouteProvider` evaluates capability compatibility before
endpoint ordering, and `LlmRouter`/clients retain validation and transport admission.
`ReliabilityEndpointSelectionPolicy` and in-memory runtime statistics describe
recent execution, not independent compatibility qualification. The
[experience roadmap](roadmap-experience-signals.md) remains planned; this design
neither implements its evidence port nor introduces another router.

BQ-2 only generates a catalogue. Later explicit promotion is:

1. Select an immutable, fresh reviewed catalogue snapshot for the exact host
   endpoint, model revision/alias, dialect and configuration.
2. Generate a **proposal diff** mapping tested facets to existing
   `LlmEndpointCapabilitiesOptions` / `LlmRoutingOptions.Profiles`. Include an
   evidence-ref manifest, expiry, supported modes and exclusions. Never synthesize
   related flags: parallel does not prove streaming tool fragments, inline image
   does not prove URI image, separate tools/schema do not prove their combination.
3. Where the current capability vocabulary cannot represent a conditional
   constraint, keep the claim in the catalogue and require an explicit host
   configuration constraint or separate future API design. Do not flatten
   conditions into a permissive boolean.
4. Host reviewer approves the diff and deployment through normal configuration
   controls. Approval snapshot binds target configuration hash and expiry. Test
   ordinary capability rejection, policy denial, budget limits and endpoint
   identity after applying it. Production does not read mutable run directories.
5. Revoke/replace through a new reviewed snapshot. Drift notifications can request
   urgent review but cannot automatically rewrite configuration. Define host
   expiration behavior before deployment: proposed default is to stop relying on
   expired approval and use the host's explicitly configured conservative profile.

Static compatibility concerns representable protocol features under recorded
conditions. Contextual performance history concerns latency, cost and success by
task/role/prompt; it remains advisory and host-selected. Neither may bypass
current capability validation, credential policy, authorization, content/tool
restrictions, budget or endpoint constraints. No automatic evidence-informed
routing is part of BQ-1 through BQ-6.

## Recurring execution and future CI

Default is manual local execution, with no schedule. Candidate cadence after
funding and quota review:

| Trigger | Bounded selection | Reason and limits |
| --- | --- | --- |
| Daily, if affordable | One already qualified canary per active endpoint/dialect; baseline stream and rotate one quirk contract | Drift signal only, one trial does not refresh the three-trial qualification. Skip when allowance unavailable; do not catch up missed days automatically |
| Weekly | Fixed rotating family/dialect cohort; selected tool/schema profiles with three trials where budget permits | Broad coverage over time; ledger records never-tested and deferred models |
| Monthly or release candidate | Representative claimed facets, required deep modalities and unresolved fixes | Full **declared** matrix, not every discovered model; explicit spend approval |
| Adapter/config/model revision change | Affected contract dependencies and existing quirk fixtures first; targeted live cohort after offline pass | New identities require new evidence; unrelated models need not be rerun |

Actual frequency is constrained by owner allowance and account terms, not these
illustrative labels. Compute plan upper bounds before enabling any recurring run.
Reasoning, multi-turn tools and media require distinct budgets. No free/unlimited
service assumption or automated trial-credit replenishment.

Future `.github/workflows/qualification.yml` starts with `workflow_dispatch`
only, protected environment approval, explicit inventory/commit/policy selectors,
minimal permissions and secrets accessible only to trusted code. Pin dependencies
and runner version. Never execute fork PR code with live credentials; no
`pull_request_target` checkout of untrusted code. Serialize jobs sharing an account
unless a host allocator is qualified. Enforce hard runner caps plus job timeout,
cancel behavior and bounded artifact upload even on partial failure.

Run only the selected qualification CLI, not the entire integration solution
(which contains paid generation probes). Separate deterministic tests in ordinary
CI remain credential-free; only offline runner tests join that suite. Do not alter
`publish.yml`. Review sanitization before CI artifact export; raw captures never
enter routine artifacts. External preservation is needed beyond CI retention.
Cron activation, funded quotas and notifications are separately approved. Notify
only meaningful drift, completed reviewable qualification, failure or required
action; no repetitive unchanged status messages.

## Graduation gates

Release readiness and permissible documentation claims are separate assessments.
No percentage of passing models defines graduation. Unsupported features are
acceptable when accurately rejected and disclosed. Baize accepting an unsupported
request, weakening canonical validation or corrupting streams is a library issue.

| Gate | Required evidence | Failure/partial disposition |
| --- | --- | --- |
| GQ-1 deterministic baseline | Existing CI package floors, supported framework/API compatibility, protocol/tool/schema/cancellation regressions and qualification offline tests pass at candidate commit | Failed gate blocks affected release scope; historical tests alone insufficient |
| GQ-2 representative chat contracts | Approved exact provider/endpoint/model/contract matrix has fresh complete cohorts for each claimed facet; baseline, stream integrity, single/sequential/parallel tools and schema modes selected where claimed | Quota/auth/availability remain Blocked, not incompatibility; absent capabilities narrow supported profile rather than fail all Baize |
| GQ-3 integrity and attribution | Canonical-schema negative tests, tool ID/result preservation, missing-tail/parity fixtures, terminal failure and disposal checks; each failure has inspectable layer and unknown attribution remains explicit | No unexplained data loss or silently accepted invalid output in supported profile |
| GQ-4 quirks and reproducibility | Every supported workaround has scope/non-scope regressions, reproduction and evidence; independent operator can resolve the bundle and reproduce the plan | Missing original artifacts/legacy narrative cannot masquerade as complete current evidence |
| GQ-5 budget/privacy/authority | Crash/resume/unknown-usage and concurrent-budget tests, zero dispatch after denial, secret ordering and redaction tests, reviewed economics sources | Unknown remaining allowance blocks live run. Unproven billing bounds prevent corresponding funded claim |
| GQ-6 truthful capability claims | All advertised release facets map to evidence and reviewed configuration; stale/contradictory/partial states disclosed, no automatic routing mutation | Remove/narrow unsupported claims; release sign-off stays human |
| GQ-7 governable transport | Independent GM-3 semantic batch/generation closure, remaining GM-4 and GM-5 B1–B12/package/consumer evidence for any full-surface governance claim | Chat-first qualification cannot close these milestones or authorize full-governance marketing |

Chat/routing may be assessed as a limited scope after GQ-1–6 with clear GM-2
chat-first boundaries. Broader stable/full-surface release requires its applicable
GM and generation gates; BQ does not change release policy or status.

### Generation graduation is separate

Use [generation-contract-matrix.md](generation-contract-matrix.md) as the source
of Implemented/Probe/Docs distinctions. At this inspected base, OpenAI image/edit,
speech and video have deterministic implementation; Gemini has implementation
and a provider-level live Interactions probe; Runway/fal.ai have deterministic
implementation plus documentation evidence. Existing live test classes are not
proof they have run. The Gemini raw provider probe does not prove the entire
Baize generation adapter/lifecycle end to end.

Generation gate GG-1 requires deterministic submit/status/result/cancel, idempotency
limitations, malformed responses, ownership/expiry, counts, error mapping and
asset URL security. GG-2 requires separate explicit paid-provider authorization
and live end-to-end `IGenerationClient` evidence per claimed operation, with
minimal assets and budget. GG-3 requires GM-3/4/5 lifecycle/handle/semantic admission
coverage for governance claims. Runway may remain live Not Tested/Blocked for
access while chat graduation proceeds. No inference, media purchase, remote job
cancellation or generation release claim is authorized by this design.

## Review disposition and outstanding approvals

The design is ready for review; offline BQ-1 can proceed after architecture
approval. Before live use, the owner must approve endpoint/model cohort, service
terms, dedicated allowance/economics and the frozen plan. Before operational
promotion, approve freshness/reliability criteria and target profile diff. Before
automation, approve funding, credential scope, retention and scheduler controls.
No recurring execution or approval is inferred from the existence of this roadmap.
