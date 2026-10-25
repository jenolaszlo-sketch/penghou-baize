# BQ-1b Stage 1 handoff — registry and offline planner

Date: 2026-10-25 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Disposition: COMPLETE.** Stage 1 delivers the authorized offline foundation:
an operator can provide a reviewed inventory and policy, select explicit models
and contracts, and obtain a deterministic, inspectable, frozen execution plan
without credentials, network access or model execution. Stage 1 does not
execute model requests and does not authorize any execution; every plan is
emitted with `executionAuthorized=false`, `qualificationPerformed=false` and an
explicit `Blocked` live-eligibility state with reason codes.

| Binding | Value |
| --- | --- |
| Base (Stage 0 accepted) | `c116b24835fac78d12d1eb601ad701bccef8d51e` |
| Implementation commit | `0a063fd3f578c3613e065b1432c3a64906d4260d` |
| Dedicated worktree | `Solo/baize-bq1b-stage1` on branch `implementation/baize-bq1b-stage1` |
| Evidence bundle | [bq1b-stage1-evidence](reviews/bq1b-stage1-evidence/README.md) |

Authorization lineage: the owner accepted BQ-1b Stage 0
(READY WITH RESTRICTIONS) and explicitly authorized Stage 1 implementation
only. This slice preserves all Stage 0 boundaries, decisions D1–D5 and the
BQ-1a accepted contracts unchanged; `git diff c116b24..0a063fd -- src/` is
empty (zero production source changes) and no historical evidence was edited.

## Scope delivered

Non-packable `net10.0` CLI at `eng/Penghou.Baize.Qualification/` (registered in
`Penghou.Baize.slnx` under `/eng/`, `IsPackable=false`, excluded from the
production dependency-closure gate by its `eng/` location):

- `inventory validate --inventory FILE` — offline validation of schema
  version, model/endpoint/profile identities, provider and protocol
  declarations, dialect/settings, capability profiles and overrides,
  advertised evidence shape, exclusions and token/economics bound shapes.
  Zero secrets, zero HTTP, zero provider construction; metadata-only
  resolution through the existing router against a denying
  `ISecretProvider`/`IHttpTransport`/`IHttpClientFactory`/provider boundary.
- `plan --inventory FILE --policy FILE --models IDS --contracts IDS --out DIR`
  — resolves selected subjects, validates the versioned policy, folds the
  streaming-integrity observation onto each invocation scenario, computes
  planned dispatch/token/cost requirements and writes frozen canonical
  artifacts (`plan.json`, `identity.json`) into a create-new directory.
  `run`, `resume`, `report`, `project`, `compare` and network discovery are
  intentionally absent and fail as `unknown-command`.

Exit-code contract preserved: 0 = offline command completed (explicitly
marked as not a qualification), 2 = invalid configuration/plan or unsupported
schema/build, 5 = internal/evidence failure. Stdout is one machine-readable
JSON summary line; stderr is a single reason code with no user input echoed.

## Files and contracts introduced

- `eng/Penghou.Baize.Qualification/`: `Program.cs` (CLI shell, bounded input
  reads, failure mapping that never echoes selectors/paths/exception text),
  `Documents.cs` (typed inventory/entry/advertised/exclusion/bound/policy
  records), `Validation.cs` (fail-closed authoritative validation),
  `OfflineResolution.cs` (denial boundary + metadata-only resolution using
  `AddLlmRouting` with a fixed `IOptionsMonitor` and one endpoint per model,
  `MaximumAttempts=1`, no fallback/routes/modules), `ContractRegistry.cs`
  (the three current BQ-1a descriptors + request-fixture digests + build
  closure binding), `CanonicalJson.cs` (`canonical-json@1`), `Planner.cs`
  (deterministic frozen plan generation).
- `eng/qualification/stage1/`: reviewed synthetic examples
  (`inventory.json`, `policy.json`, `canonical-vectors.json`), embedded into
  the qualification test project.
- `tests/Penghou.Baize.Qualification.Tests/Stage1*.cs`: 32 deterministic
  acceptance tests.
- Wiring only: solution entry, `.gitignore` artifacts path,
  `InternalsVisibleTo "Penghou.Baize.Qualification"` on the shared contracts
  project (the only internal accessibility added, per plan), test-project
  reference to the CLI and embedded fixtures.

Dependency note: the CLI adds `Microsoft.Extensions.Configuration.Json`
10.0.9 — matching the repository's pinned Microsoft.Extensions version and
required by the audited fixed-options-monitor seam
(`AddLlmRouting(IConfiguration)`). It is tool-only; the production dependency
closures remain exactly the previously verified 12 packages
(`eng/verify-transport-dependencies.ps1` re-verified).

## Semantics preserved from the accepted design

- Contracts are consumed, never duplicated: ids, fixture ids, assertion
  lists, dispatch budgets (1) and the 512 output cap come from
  `QualificationContractsV3`/`QualificationFixtures`; the registry rejects
  historical versions (`chat.baseline@2`, `tools.single@2`,
  `chat.streaming-integrity@1`) for plans and exclusions.
- Streaming integrity observes the same dispatch: it never creates a case,
  never adds a dispatch slot, and fails closed (`streaming-requires-parent-
  scenario`) when selected without an invocation contract. Excluding it is
  recorded per case (`streamingExclusion`) without changing dispatch counts.
- Requested versus resolved configuration are separate subject fields;
  mutable aliases and null revisions stay explicitly unknown
  (`model-revision-unknown`, `protocol-revision-unknown`,
  `protocol-revision-evidence-unverified`); provider-identity provenance and
  canonical correlation remain `NotObserved`; applicability follows the R2
  decision (OpenAI/Anthropic `Applicable`, Ollama/Gemini `Undetermined`).
- Different hosting services/accounts/profiles never share subject identity,
  even with identical wire destinations.
- `canonical-json@1`: ordinal property ordering, plain invariant decimal
  numbers (no scientific notation; trailing zeros removed), decimal-string
  money normalized through one writer, ordered sequences preserved, and set
  normalization restricted to schema-owned arrays (entries, advertised,
  exclusions, bounds, capability sets, endpoint allowlist).
- Policy v1 keeps the reviewed defaults (concurrency 1, retries 0, 10-minute
  run deadline, 120-second attempt, 30-second idle, 5-second cleanup grace,
  1 MiB/4 MiB/64 KiB/16 MiB byte ceilings) and rejects zero-as-unlimited,
  contradictory bounds, unsupported features (automatic retries, capture
  policies) and unknown schema versions.
- No credential values/hashes, private prompt text or workspace paths enter
  the plan; secret names appear only as nonsecret slot references, and
  fixture prompts are represented by digests of repository-owned synthetic
  fixtures only.

## Deterministic conformance

The acceptance suite (`-class '*Stage1*'`, 32 tests) covers the four
required groups:

- **A — inventory and resolution (10):** separate immutable subjects,
  hosting-service identity separation, explicit/inherited settings and
  effective attempt ceilings, unknown/empty/duplicate selectors, alias and
  revision unknowns, endpoint/exclusion/profile/provider/protocol failures,
  all-excluded rejection.
- **B — contract mapping (8):** registry equals the accepted descriptors and
  fixture digests, plan records exact assertions/fixtures/budgets, historical
  versions cannot enter plans or exclusions, streaming shares the parent
  invocation, exclusion of the observation, deterministic case/slot ids,
  selection and registry identity changes alter plan hashes.
- **C — canonicalization (7):** byte-identical repeat planning with
  plan-hash/identity agreement, output immutability
  (`output-already-exists`), equivalent property/set reordering invariance,
  meaningful order preservation, configuration-change identity effects,
  invariant culture stability, embedded canonical vectors.
- **D — boundaries and privacy (7):** zero secret reads/HTTP/provider
  constructions for both commands (verified against a denying boundary, with
  sentinel environment secrets), invalid inputs produce no artifacts, no
  credential/prompt/path leakage, missing economics recorded as unknown
  (never zero/free) with and without reviewed bounds, planned eligibility
  never implies execution permission, unknown schema versions and commands
  fail explicitly.

Negative tests observe the denial ports' counters and sentinel leakage, not
merely return values.

## Build and test results

Runtime: Windows, .NET SDK 10.0.401, Microsoft.NETCore.App 10.0.12; commands
ran with `MSBuildEnableWorkloadResolver=false`, `BAIZE_RUN_LIVE_TESTS=0` and a
nonexistent `BAIZE_ENV_FILE`; no live provider requests, secret reads or
publication occurred.

| Gate | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings/0 errors |
| Production dependency closures | 12/12 verified, exact public transport packages, no Hufu |
| Full deterministic solution tests | **2,028 passed, 28 existing skips, 0 failures** across 26 project/framework executions; includes the 32 new Stage 1 tests plus the unchanged 183-test BQ-1a qualification suite |
| Solution format | Only the three pre-existing, previously documented whitespace findings at `src/Penghou.Baize.Tools/Schema/JsonSchemaGenerator.cs` lines 19/44/52 remain (Git blob unchanged, same as the BQ-1a handoff); all new files are format-clean |
| Local package validation | 12 existing packages pack cleanly from the solution; output stays in ignored artifacts; no publication |
| `git diff --check` | Clean |

One transient observation, recorded for reproducibility: during the first
full parallel solution run, `Penghou.Baize.Tests
.StreamAsync_EmitsPrivacySafeIntegrityActivity` (net10.0) failed once. It
passed in three isolated reruns and in a complete second full-solution run;
no product or test source related to that test was modified by Stage 1. It is
reported as a load-sensitive flake observation, not a Stage 1 regression.

## Evidence binding

`docs/reviews/bq1b-stage1-evidence/` preserves the exact commands, input and
output SHA-256 hashes, the double-run byte-identity observation and eight
deterministic fail-closed rejection records produced by the Release CLI built
from the implementation commit. The sample plan binds
`sourceRevision 0.3.0-preview.7+0a063fd3f578c3613e065b1432c3a64906d4260d`,
the contract registry hash
`sha256:ce2503aca92774ea74878290ee2a9b95d985ba24e8eae66876db5654f1d90db8`,
inventory hash
`sha256:7ca89e46563076d7c395f881a34ad08fe01c71940a4663954a9f3145c4960411` and
policy hash `sha256:eb25991b2a13eddffff43bd3d7324ee74258d1c4c68090aa3832df3ba7b07618`.
These are offline tooling records, not model qualification evidence.

## Known restrictions and remaining eligibility gaps

- Every planned case is `Blocked`; the run-level blocker set always includes
  the Stage 2–5 admission gaps (`stage2-durable-admission-not-implemented`
  through `stage5-lifecycle-and-settlement-not-implemented`). Stage 1 grants
  no admission, journal, budget or dispatch capability.
- Real-network profiles carry `wire-attempt-bound-unproven` and
  `wire-and-preallocation-byte-bounds-unproven` (S0-G1/S0-G6); only
  `SyntheticFake` profiles avoid them, per Stage 0 decision D1. Loopback
  development profiles are representable but equally blocked by the stage
  gaps.
- Protocol revision evidence is never manufactured: a declared `apiVersion`
  still blocks as `protocol-revision-evidence-unverified`; a missing one
  blocks as `protocol-revision-unknown`. Unknown model revisions stay unknown.
- Economics and allowances are planning references only: missing
  economics/allowance/cost bounds block paid eligibility (`economics-unknown`,
  `allowance-unverified`, `cost-envelope-unknown`,
  `cost-reservation-bound-unknown`); present bounds still block as
  `token-bound-enforcement-unverified` / `economic-bound-evidence-unverified`
  until Stage 2/5 enforcement exists. Unknown is recorded as null, never zero.
- Exit codes 3 (contract failure) and 4 (blocked run) are unused by Stage 1
  because no execution exists; 130 (operator cancellation) is likewise
  reserved. Offline `plan` success is exit 0 with explicit
  not-a-qualification markers.
- The `plan` output directory is created create-new and never overwritten,
  but Stage 1 makes no durability guarantee (flush/fsync, torn-write
  behavior, exclusive writer): all durability semantics are Stage 2 work.
- `inventory validate` reports model identity and inventory hash; it does not
  emit resolved capability snapshots (that is `plan`'s role).
- The CLI's `identity.json`/`plan.json` pair is a frozen planning artifact,
  not the Stage 2 manifest/journal format.

## Stage 2 dependencies

BQ-1b Stage 2 (durable store and budget state) consumes this frozen plan and
must, without reinterpreting configuration or contract semantics:

1. Treat `plan.json` bytes and `planHash` as the admitted planning input;
   bind the manifest to the plan hash, `source.build` closure,
   `contractRegistryHash`, `inventoryHash` and `policyHash`.
2. Implement create-new/flush/atomic-replace durable storage, the
   sequenced/hash-linked event journal, exclusive writer and the
   reservation/intent/result/settlement state machine over these case and
   trial-slot ids.
3. Reserve against `requirements` (semantic/neutral/backend counts, token
   and cost bounds with their unknown states) per subject/case/trial.
4. Carry the blocker codes forward unchanged; eligibility decisions remain
   fail-closed and cannot be overridden by an operator switch.

No Stage 2 work was started here; no durable store, journal, budget
reservation or execution state exists in this slice.
