# BQ-1b Stage 2 handoff — durable qualification execution slice

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Disposition: COMPLETE.** Stage 2 delivers the authorized durable execution
slice: a frozen Stage 1 plan is verified, bound to durable Zhinu workflows,
executed through the real Baize routed adapter with a deterministic synthetic
terminal transport, evaluated by the unchanged BQ-1a V3 qualification
contracts, recorded as verifiable Siming evidence, and reported offline as
JSON plus Markdown. No real network, credential, paid execution or publication
occurred.

| Binding | Value |
| --- | --- |
| Base (Stage 1 accepted) | `67e85978d0c9a8bdf7ef9cfdde7463f501c40126` |
| Implementation commit | `6385b0f1aeeccd039e3bff8828d7050ebfbc7e8f` |
| Dedicated worktree | `Penghou.Baize.stage2` on branch `implementation/baize-bq1b-stage2` |
| Evidence bundle | [bq1b-stage2-evidence](reviews/bq1b-stage2-evidence/README.md) |

`git diff 67e8597..6385b0f -- src/` is empty: zero production source changes.
The revised [implementation plan](model-qualification-bq1b-implementation-plan.md)
replaces the earlier custom JSONL/journal/workflow-engine stages with the
pinned Penghou components.

## Architecture delivered

**Reuse, not duplication.** The durable workflow lifecycle is
`Penghou.Zhinu.Sqlite 0.2.0-preview.4` (step commits, leases, restart,
cancellation, stable run identity). The evidence ledger is
`Penghou.Siming.Sqlite 1.0.0` (append-only hash chain, idempotent appends,
expected-head concurrency, checkpoints, verification). Baize contributes only
the qualification composition: the frozen-plan binding, the case workflow,
the routed synthetic execution, the budget accounting and the report. Both
packages are tool-only `eng/` dependencies; the 12 production closures are
unchanged and re-verified.

**Run layout** (`artifacts/qualification/<run>/`): `manifest.json` (identity
binding), `plan.json`/`identity.json` (byte-identical plan copies),
`evidence.db` (Siming), `workflow.db` (Zhinu), `checkpoint.json`,
`reports/summary.json` + `reports/report.md`.

**Workflow** (`bq1b-qualification-case@1`, stable run id =
`SHA-256(planHash|caseId|trialId)`): `bind` (registry/fixture consistency,
fails before any secret) → `invoke-attempt` (CAS budget reservation, durable
dispatch intent, routed synthetic invocation, attempt-finished evidence; a
fresh attempt id per execution) → `settle-usage` (observed or `spent-unknown`)
→ `finalize-trial` (trial evidence + ledger verification). Contract outcomes
complete as failed trials and are never restarted; only infrastructure
failures (evidence I/O, interruption, timeout, lease) are recoverable, via
Zhinu step restart with a stable operation id, always as a distinct recorded
attempt.

**Recovery.** Resume re-opens both stores, re-verifies plan/build/registry
identity, reuses committed Zhinu steps, restarts recoverable failed steps,
reconciles interrupted attempts idempotently (reserved-without-intent →
released; intent-without-result → interrupted + conservative `spent-unknown`;
finished-without-settlement → settled from the recorded observation) and
re-appends run summaries under digest-derived idempotency keys so history is
never rewritten and a repeated resume never duplicates an event.

**Budget.** The frozen policy is the envelope; the Siming budget stream is the
committed consumption record. Remaining allowance is recomputed on every
reservation and the expected-head append makes over-allocation impossible
(verified by an 8-way race admitting exactly one). A model call consumes its
allowance; unknown usage retains conservative liability; nothing replenishes
on restart. Token/cost ceilings are enforced when present in policy; unknown
economics stay unknown.

**Failure taxonomy.** Evidence distinguishes contract-assertion failure from
`provider-unavailable`, `rate-limited`, `authentication`, `authorization`,
`timeout`, `caller-cancelled`, `budget-denied`, `host-authority-denied` and
evidence I/O; a 503 synthetic backend records `Blocked`/
`provider-unavailable`, never a model capability failure.

**Privacy.** Evidence payloads are content-free typed records (hashes, opaque
IDs, counts, assertion ids/statuses, usage, references); no prompts, raw
responses, tool arguments, credentials, exception text or private URLs are
persisted (verified by byte-scanning every run-directory file).

## CLI commands added

- `run --plan FILE --out DIR --models IDS --contracts IDS [--trial N]` —
  synthetic-only; real-network profiles and excluded cases are refused before
  any secret reference.
- `resume --run DIR` — replays committed work, recovers interrupted attempts,
  never replenishes budgets.
- `report --run DIR` — offline JSON + Markdown from verified evidence, with
  per-case configuration, contract, outcome, failed assertion ids, attempts,
  limitations and evidence references; exit precedence matches the run.

## Acceptance evidence

The ten required deterministic scenarios (success; contract failure with
preserved negatives; cancellation/incomplete observation; restart after a
committed step; failure between workflow completion and the Siming append;
repeated append identity; re-execution as a distinct attempt; evidence-storage
failure with no green report; budget exhaustion and unknown usage; multi-case
isolation) are covered by 25 deterministic tests using the real pinned stores
and fault injection at documented seams — details and hashes in the
[evidence bundle](reviews/bq1b-stage2-evidence/README.md). Full solution:
2,053 passed, 28 existing skips, 0 failures.

## Known limitations

- Synthetic execution exists for `openai-chat-completions` only; other
  synthetic protocols remain explicitly blocked (`synthetic-transport-not-implemented`).
  Real-network profiles are refused (`real-network-not-authorized`); the HTTP
  admission audit, wire-attempt accounting, secret ordering and full usage
  settlement remain the next stage (revised plan stage 3) before any live
  eligibility.
- Cancellation evidence is conservative: a mid-stream interruption records
  the intent and an interrupted/incomplete observation; a durable
  `Cancelled` run status is reflected in the report's workflow status.
- SQLite files are not byte-deterministic; evidence integrity is carried by
  the Siming hash chain and checkpoint, not by file hashes.
- The BQ-1a F1 adapter ordering defect remains untouched and separately
  attributed; nothing in this stage weakens or retries it.
- Stage 1 historical plans are not executable by the Stage 2 runner because
  the runner build closure gained the pinned components; plans are
  regenerated with the current build (by design — changed build fails closed).

## Next stage

Stage 3 (real-transport admission and lifecycle audit) and the controlled
real API/provider/model qualification through the actual adapters — per the
revised implementation plan. No Marang, MCP, scheduling, catalogue or
promotion work belongs to BQ-1b.
