# BQ-1a closure — ACCEPT WITH FOLLOW-UP

Date: 2026-10-25 (Asia/Manila). Repository:
`jenolaszlo-sketch/penghou-baize`.

**Disposition: ACCEPT WITH FOLLOW-UP.** The owner explicitly accepts the remaining
risks within BQ-1a's documented qualification-contract scope and authorizes merge
after normal mechanical checks. No further independent BQ-1a review is requested
or initiated. Earlier review/handoff dispositions remain preserved historical
records; this owner decision closes their pending BQ-1a acceptance gate without
rewriting their evidence or implying an author-disjoint sign-off.

Acceptance applies to the shared deterministic qualification contracts and
integration extraction. It establishes no live model/provider capability,
provider identity provenance, remote cancellation/billing guarantee, package
release or GM milestone closure. BQ-1b is at the
[implementation-plan examination stage](model-qualification-bq1b-implementation-plan.md),
not authorized for implementation or live use.

## Immutable implementation and evidence bindings

| Role | Exact commit |
| --- | --- |
| Original design | `355685a04931ec0d87ae3b74232b123e9d04d86e` |
| Original implementation | `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` |
| Original implementation handoff | `dab7c8f7efe3274e7285fc97bbc4f1d407633857` |
| Original independent review and 16 original probe outputs | `b169d6ab66a211b611183d1d44e97f0419a20b06` |
| First versioned remediation implementation | `68cd12a8ca1a019f7b863db87a1f62e6c10eebdd` |
| First remediation handoff | `2898917f4dfefc476c184d281cb68c0c59513e53` |
| R1/R2 technical re-review and 33 original challenge outputs | `2a5ab3efc9155fad30a88511acde7a4d6fbfa61a` |
| **Final accepted qualification implementation** | **`123dd86249eba57be700d5e32b486a7527e06474`** |
| **Final handoff and corrected challenge evidence** | **`7c986ef0964b58c6d3d18753eb3e3c43ff1ccb64`** |
| Main base checked before integration | `b8f86893851726eb3b40a556435f9202b2460673` |

Preserved documents:
[original review](reviews/bq1a-focused-independent-review.md),
[technical re-review](reviews/bq1a-remediation-independent-rereview.md),
[final R1/R2 handoff](model-qualification-bq1a-r1r2-handoff.md) and
[decision record](model-qualification-bq1a-remediation-decisions.md).
The re-review discloses that the same chat authored remediation; the owner
accepts that review-provenance limitation. The acceptance decision is the owner's
instruction, not a new AI-generated independent approval.

## Findings and disposition

| Finding | Final disposition and evidence |
| --- | --- |
| F2 — expected response shape | **CLOSED in current contracts.** Baseline rejects undeclared native calls; tools-only rejects substantive answer text. Whitespace/reasoning policy is explicit. Original counterexamples remain historical negatives and reject under V3. |
| F3 — router/dispatch evidence | **CLOSED at the BQ-1a observation boundary.** Exactly one final successful attempt is required when diagnostics are present/required; zero/failed/premature/contradictory snapshots reject and retain safe counts. Physical admission remains BQ-1b work. |
| F4 — cancellation attribution | **CLOSED.** Foreign/bare OCE is Inconclusive/ambiguous, known deadline timeout is Blocked and caller cancellation is Cancelled. Missing final observations remain NotObserved; raw failures survive operational dispositions. |
| D1 — native validity versus identity | **CLOSED as a protocol-aware design decision, including R2 correction.** Native validity, canonical turn-local correlation and provider provenance are distinct. Required normalized IDs remain strict for OpenAI/Anthropic; optional/index-based correlation is scoped. Successful native/canonical checks never promote NotObserved provenance. |
| R1 — nullable completion erasure | **CLOSED at `123dd86`.** Null retains the last explicit value; later true/false may update it. Three challenge false passes now fail; positive intermediate/metadata controls remain valid. Permanent baseline/tool/routed tests cover all transitions. |
| R2 — unversioned Ollama inapplicability | **CLOSED at `123dd86`.** Unbound revision yields Undetermined / `protocol-revision-unbound`; provenance remains NotObserved. ID-present and ID-absent real-adapter fixtures preserve native/canonical success without claiming provider identity. |
| F1 — OpenAI co-terminal emission ordering | **OPEN, ACCEPTED FOLLOW-UP.** Existing adapter emits finish before tool deltas from the same wire choice. Strict sequence continues to fail. This is a fixture-verified adapter defect, not unsupported-model evidence. No adapter fix or qualification exception is authorized here. |

F1 reproductions are the `F1-real-openai-coterminal` challenge record and its
split-finish control in the unchanged
[33-case source](reviews/bq1a-remediation-rereview-probes/Program.cs), plus
`F1_CoTerminalWholeCallAndFinalFragmentStillExposeAdapterDefect` in
[QualificationProviderV3Tests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs).
The latter covers whole-call/final-fragment and direct/routed cases. The original
review also preserves the first reproduction. Ownership is a separately
authorized production OpenAI adapter change with deterministic regressions;
BQ-3 may formalize its quirk reference. BQ-1b/BQ-2 must retain that attribution
and must not convert it to Unsupported or relax the order assertion.

## Contract/evidence preservation

Current accepted descriptors are `chat.baseline@3`, `tools.single@3` and
`chat.streaming-integrity@2`, bound to the final implementation above.
Historical `chat.baseline@2`, `tools.single@2` and
`chat.streaming-integrity@1` retain their original meaning and executable behavior,
including known false acceptances. No evidence cohort is upgraded by this
acceptance. Current execution must select explicit current descriptors/builds;
historical readers/replays keep old versions separate.

The preserved [original probes](reviews/bq1a-independent-probes/) retain all 16
outputs. The [re-review bundle](reviews/bq1a-remediation-rereview-probes/) retains
all 33 original challenge outputs and source. The separate
[corrected replay](reviews/bq1a-r1r2-remediation-evidence/) retains all 33 corrected
records, binding/hash manifest and executable comparison. Exactly five records
change: three R1 records change only the expected dispositions/completion
assertions, and two Ollama records change only applicability/code. The other 28
records, including F1, F2/F3/F4 and positive controls, remain unchanged.

Final qualification evidence is **183/183 passed**, including 46 focused R1/R2
cases. Neither that green result nor owner acceptance turns fixture evidence
into live qualification. Existing reviews/probes, frozen V2 contract sources
and previous handoffs remain unchanged; corrected decisions are commit-bound.

## Accepted limitations and future ownership

| Boundary | Disposition / owner |
| --- | --- |
| Semantic invocation/activity/router counts do not prove physical sends or hidden retries | BQ-1b admission/path audit, with explicit unsupported-path/live eligibility gates. |
| Input/billing/byte bounds, total/idle deadlines, unknown liabilities and bounded cleanup | BQ-1b controlled execution; no hard external billing or arbitrary-code containment promise. |
| Normalized/collector IDs do not prove provider wire provenance; Ollama revision is unbound and its current adapter drops optional IDs | BQ-1b immutable subject/protocol binding and separate adapter/BQ-3 wire evidence. ID forwarding is not part of BQ-1a closure. |
| Flattened content/reasoning/tool reconstruction and scalar conservation do not establish full ordered parts/signature/continuation or detect losses before normalization | BQ-4 full-fidelity/shared sequential/parallel/continuation scenarios. |
| Immutable evidence cohorts, corrections, catalogue state, legacy imports and attribution projection | BQ-2; no automatic current-claim promotion from historical success or aggregate Succeeded. |
| Review provenance lacks an author-disjoint final sign-off | Explicitly disclosed and accepted by the owner for this scoped closure. No repeat review initiated. |

## Mechanical integration checks

The implementation branch was clean at `7c986ef` before closure/plan documents.
Remote refs were refreshed; main was the ancestor listed above, with no competing
commits, no open PR and no branch protection requiring another merge route.
The main checkout has one pre-existing, uncommitted roadmap addition; preserve
it outside this commit, temporarily isolate it for integration and restore it
afterward. Do not commit or publish that unrelated edit as part of BQ-1a.

Commands ran with `BAIZE_RUN_LIVE_TESTS=0`, an explicit nonexistent
`BAIZE_ENV_FILE`, and command-local `MSBuildEnableWorkloadResolver=false`:

```powershell
dotnet build Penghou.Baize.slnx --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet
./eng/verify-transport-dependencies.ps1
dotnet format Penghou.Baize.slnx --verify-no-changes --no-restore --verbosity quiet
dotnet test Penghou.Baize.slnx --configuration Release --no-build --verbosity quiet --logger trx
dotnet pack Penghou.Baize.slnx --configuration Release --no-build --no-restore --disable-build-servers -m:1 --output artifacts --verbosity minimal
./docs/reviews/bq1a-r1r2-remediation-evidence/Verify-ChallengeReplay.ps1
git diff --check
```

| Gate | Result |
| --- | --- |
| Release build | Passed, 0 warnings/errors |
| Production dependency closures | All 12 verified, exact existing public transport packages, no Hufu |
| Full deterministic solution tests | **1,996 passed, 28 existing skips, 0 failures**, 26 project/framework executions; includes **183 passed** qualification tests |
| Solution format | Passed after local checkout line-ending normalization of the three previously documented locations; normalized Git source blob remains `14d5fd54e053d0dabf9c7f38769d432d85a67b12`, identical to main/design/accepted source |
| Local package/API validation | Passed for the 12 existing packages; output stays in ignored artifacts, no publication |
| Corrected challenge/preservation checks | 33/33 verified with exactly five expected changes; preserved review/probe blobs unchanged |
| Git whitespace/scope | Passed; closure adds documentation only, no qualification/production semantics, APIs, dependencies or fixture changes |

The TRX summary counts skip outcomes from individual `UnitTestResult` entries;
the runner's aggregate `notExecuted` attribute is zero even when those outcomes
are present. Existing skips are fixture capability skips, not new passes. The
first default multi-node local pack attempt exited without diagnostics; the
single-node command above passed. These are local mechanical results, not a
new coverage-floor, cross-platform CI or online vulnerability audit. No live
provider requests, raw credential reads or package publication occurred.

## Integration outcome

Local main was fast-forwarded from
`b8f86893851726eb3b40a556435f9202b2460673` to closure/plan commit
`17cd440c293975a501784bd9903ed5b53818c52d`. The pre-existing roadmap addition
was temporarily stashed, restored, compared exactly with new HEAD plus the
original addition, and left uncommitted. The verified temporary stash was
removed. The BQ-1a branch is clean; main's sole working-file change is that
preserved user edit. This documentation-only follow-up records the outcome;
the accepted implementation/evidence bindings above remain unchanged.

Remote integration is **not performed**: the attempted atomic push of main and
`implementation/baize-bq1a` was rejected before execution by automatic approval
review. Its stated reason was that local merge authorization did not explicitly
authorize mutation of both remote branches. No bypass or alternate remote
mutation was attempted. `origin/main` remains at the checked base; updating
remote refs requires explicit owner approval. No publication workflow was
triggered, no tags were pushed, no packages were published and no live
qualification was run.
