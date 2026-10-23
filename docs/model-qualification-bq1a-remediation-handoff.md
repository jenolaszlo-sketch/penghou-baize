# BQ-1a focused remediation handoff

Date: 2026-10-23 (Asia/Manila).

**Disposition: REMEDIATION COMPLETE, INDEPENDENT RE-REVIEW REQUIRED.**

F2-F4 have qualification-only fixes and permanent negative tests. D1 has an
explicit protocol/applicability/provenance decision and cross-adapter fixtures.
The additional JSON, repair and opaque-ID decisions are implemented. F1 remains
an open, fixture-confirmed production adapter defect; it was not accommodated or
fixed here. Passing tests do not establish BQ-1a acceptance or model capability.

## Exact source and evidence binding

Repository: `jenolaszlo-sketch/penghou-baize`. Local branch:
`implementation/baize-bq1a`, worktree `Solo/baize-bq1a`.

| Role | Exact commit |
| --- | --- |
| Original qualification design | `355685a04931ec0d87ae3b74232b123e9d04d86e` |
| Reviewed implementation | `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` |
| Review baseline / original handoff | `dab7c8f7efe3274e7285fc97bbc4f1d407633857` |
| Original review and probe preservation, before remediation | `b169d6ab66a211b611183d1d44e97f0419a20b06` |
| Remediation implementation and decisions | **`68cd12a8ca1a019f7b863db87a1f62e6c10eebdd`** |

This document is the documentation-only follow-up to the remediation commit.
The delivery message records that final handoff tip. The tested final source
tree was committed as `68cd12a`; no semantic source edits followed those final
runs. Review `b169d6a..68cd12a` for the eight-file remediation, and
`dab7c8f..68cd12a` to include the preserved original independent review artifacts.
The original design, original handoff and review text have not been rewritten.

Start with the [independent review](reviews/bq1a-focused-independent-review.md),
its [unchanged executable probes](reviews/bq1a-independent-probes/Program.cs),
[original outputs](reviews/bq1a-independent-probes/results.jsonl), and the
[remediation decisions](model-qualification-bq1a-remediation-decisions.md).

There are no changes to `src/`, production project/package references,
`Directory.Build.props`, provider behavior, production APIs or capability flags.
The only additional project references are **test-project** references to the
existing Gemini and Ollama adapters for deterministic protocol fixtures. Nothing
was merged, pushed or published. No live provider calls, paid qualification,
automatic profile promotion or BQ-1b implementation occurred.

## Version and entry-point migration

| Historical, preserved behavior | New qualification behavior |
| --- | --- |
| `chat.baseline@2` | `chat.baseline@3`: full response-shape and repair-path checks, corrected execution evidence |
| `tools.single@2` | `tools.single@3`: no ordinary answer text, unique JSON members, explicit protocol correlation and repair provenance |
| `chat.streaming-integrity@1` | `chat.streaming-integrity@2`: corrected router/dispatch observations, protocol-aware tool correlation and known part-index consistency |

The legacy `QualificationContracts`, `QualificationScenarios`, fixtures,
`IntegrityTelemetry`, original scenario tests, original provider tests and
`ScenarioFixture` have the same Git blobs as `dab7c8f`. All original 47 tests
remain executable. The original 16-probe output still reproduces exactly under
the old identifiers, including its recorded undesired passes/rejections.

The corrected entry points are `QualificationScenariosV3` with
`QualificationContextV3`. Both original public integration test methods and
their capability traits remain. Their wrappers explicitly select the new
contracts, still configure single-dispatch routing and retain existing live/tool
opt-ins. Other integration capabilities are unchanged. The prompts, schema,
temperature and cap are unchanged, so fixture IDs remain at their existing
versions; changed assertion semantics have new contract IDs.

Provider provenance is a separate `ToolIdentityEvidence` observation. Native
tool/canonical-correlation success does not establish provider-emitted identity.
At this event boundary that provenance is always NotObserved; Ollama's native
protocol marks the wire-ID facet NotApplicable rather than failing the model.
Unknown tool protocol binding blocks before dispatch. OpenAI/Anthropic missing
stream IDs still fail; no global missing-ID tolerance was introduced.

## Finding dispositions and review entry points

All statuses below describe implementation disposition pending independent
re-review, not reviewer acceptance.

| Finding / concern | Disposition and evidence |
| --- | --- |
| F2: tool answer text and undeclared baseline tools | Remediated under new IDs. `QualificationScenarioV3Tests.NewVersionsKeepFixturesAndOldBehaviorExplicitlySeparate` uses the original counterexample shapes and checks legacy success versus current failure. Formatting whitespace and independent reasoning remain valid. |
| F3: zero/failed router attempts, overwritten contradictory snapshots | Remediated. Exactly one successful terminal snapshot is required when routing is used; supplied contradictory diagnostics fail even when observation was optional. Each numeric snapshot remains in safe results. Tests cover zero/failed/unknown outcomes, multiple/premature/replaced snapshots, multiple attempts, routed success and 503 without retry. |
| F4: foreign cancellation reported as timeout | Remediated. Foreign/bare cancellation is Inconclusive; explicit caller and deadline tokens, preflight cancellation, disposal and typed timeout have separate tests. Final-response-dependent checks remain NotObserved when no final response exists. |
| D1: native validity versus canonical identity versus provider provenance | Implemented explicit decision. Required-ID protocols keep their requirement; Ollama uses turn-local index/collector correlation; Gemini's optional/generated normalized ID is not called provider provenance. Real-adapter fake-HTTP fixtures cover Ollama, Gemini with/without wire ID and OpenAI with/without emitted ID. |
| Duplicate JSON members | Reject recursively, including escaped duplicate names and objects in arrays; both member orders and identical duplicate values fail uniqueness. Do not select a last value. Invalid syntax does not invent a duplicate-key observation; downstream schema/value checks are NotObserved until JSON is unambiguous. |
| Repair metadata | Native profile requires bypassing the repair path. Flags, nonempty attempt lists or detailed diagnostics block a native pass using `repair-path-observed`. This denotes composition/path evidence, not proof that text changed or model output was invalid. Contradictory diagnostics-only and tolerant recovery metadata are negative fixtures. |
| Opaque ID restrictions | Removed invented maximum-length/character restrictions in the new version. Nonempty IDs are preserved/compared ordinally without trimming. Long, spaced and control-character examples pass correlation without leaking the ID. Empty supplied IDs and conflicts still fail. |
| Part-index counterexample | New integrity rejects a known tool part index moving between fragments. Full ordered-part and continuation fidelity remains outside this scalar contract. |
| F1: OpenAI co-terminal ordering | **Preserved, open adapter defect.** `QualificationProviderV3Tests.F1_CoTerminalWholeCallAndFinalFragmentStillExposeAdapterDefect` has four direct/routed, whole/final-fragment cases. Valid schema/value coexist with failed sequence. A single public event containing payload and finish still passes. No adapter source changed. |

Implementation locations:

- [QualificationContractsV3.cs](../tests/Penghou.Baize.Qualification.Contracts/QualificationContractsV3.cs): new versions, explicit protocol binding and separate safe identity/router evidence.
- [QualificationScenariosV3.cs](../tests/Penghou.Baize.Qualification.Contracts/QualificationScenariosV3.cs): execution/classification, semantic assertions, JSON uniqueness, correlation, strict sequence and telemetry observation.
- [QualificationScenarioV3Tests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationScenarioV3Tests.cs): original counterexamples and additional identity/router/cancellation/provenance tests.
- [QualificationProviderV3Tests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs): real adapters/router over in-memory HTTP; no socket opens.
- [QualificationV3RegressionTests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationV3RegressionTests.cs): terminal, semantic, schema, accounting, missing-observation and interruption negatives for the new versions.

## Verification record

Environment: Windows x64, .NET SDK 10.0.401, .NET runtime 10.0.12, xUnit v3
in-process runner 1.1.0. Commands ran from the dedicated worktree. Set for each
test-bearing shell:

```powershell
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1a_remediation_no_environment_file__'
$env:MSBuildEnableWorkloadResolver = 'false'
```

The nonexistent explicit environment-file path prevents ancestor dotenv loading.
All provider fixtures inject handlers that return synthetic content without
opening sockets. The two integration methods exit through the live opt-out.

| Exact command / check | Final observed result |
| --- | --- |
| `dotnet restore tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj --ignore-failed-sources --verbosity quiet --disable-parallel -m:1 -p:NuGetAudit=false` | Exit 0, cached dependencies restored |
| `dotnet build tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet` | Exit 0, zero warnings/errors |
| `dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo` | Exit 0: **138 total, 138 passed, zero failed/skipped/not-run**. Original 47 plus 91 new cases |
| `dotnet build tests/Penghou.Baize.IntegrationTests/Penghou.Baize.IntegrationTests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet` | Exit 0, zero warnings/errors |
| `dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -trait 'Capability=Baseline' -trait 'Capability=Tools'` | Exit 0: exactly two tests, both skipped, zero failures |
| `dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -list tests -trait 'Capability=Baseline' -trait 'Capability=Tools'` | Exit 0: exactly the two original entry points listed below |
| `dotnet build docs/reviews/bq1a-independent-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet` | Exit 0, zero warnings/errors |
| Probe execution/comparison below | Exit 0: **all 16 preserved structured outputs match exactly** |
| Scoped format verification below, in both solutions | Exit 0, no changes required |
| `git diff --check`, staged whitespace check | Passed |
| Git blob comparison for seven frozen source files against `dab7c8f` | All identical |
| Diff of `src/` and `Directory.Build.props` against `dab7c8f` | Empty |
| Diff of `docs/reviews/` against preservation commit `b169d6a` | Empty |

Discovery returned exactly:

```text
Penghou.Baize.IntegrationTests.LiveProviderTests.NativeToolSmokeTest_ReturnsSchemaValidArguments
Penghou.Baize.IntegrationTests.LiveProviderTests.StreamingSmokeTest_ReturnsExpectedContentAndDiagnostics
```

Probe comparison used the original program unchanged:

```powershell
$bqActual = @(dotnet docs/reviews/bq1a-independent-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll)
if ($LASTEXITCODE -ne 0) { throw 'Probe execution failed' }
$bqExpected = @(Get-Content docs/reviews/bq1a-independent-probes/results.jsonl)
if ($bqActual.Count -ne 16 -or $bqExpected.Count -ne 16 -or
    [string]::Join([Environment]::NewLine, $bqExpected) -cne [string]::Join([Environment]::NewLine, $bqActual)) {
  throw 'Historical probes changed (case/order-sensitive comparison)'
}
```

For a fresh clone/build directory, first restore that probe project with its
provided `NuGet.Config`; its runtime references require the Release qualification
build above. Historical outputs are retained, not regenerated to match new rules.
Working-file SHA-256 of the preserved results is
`607b74b32f85615a9a47064a6fad08c4c01df8036ba36989ec584f692d11f41a`;
the Git blob remains authoritative across checkout line-ending conventions.

Scoped formatting was applied and verified only to remediation files:

```powershell
$bqFormatFiles = @(
  'tests/Penghou.Baize.Qualification.Contracts/QualificationContractsV3.cs',
  'tests/Penghou.Baize.Qualification.Contracts/QualificationScenariosV3.cs',
  'tests/Penghou.Baize.Qualification.Tests/QualificationScenarioV3Tests.cs',
  'tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs',
  'tests/Penghou.Baize.Qualification.Tests/QualificationV3RegressionTests.cs'
)
dotnet format Penghou.Baize.slnx --verify-no-changes --no-restore --include $bqFormatFiles --verbosity quiet
dotnet format Penghou.Baize.IntegrationTests.slnx --verify-no-changes --no-restore --include tests/Penghou.Baize.IntegrationTests/LiveProviderTests.cs --verbosity quiet
```

Earlier local attempts were not all green: default multi-node restore exited
without useful diagnostics; single-node restore then reported NU1900 because
the NuGet vulnerability endpoint was unavailable. Cached restore with the
command-local audit option above succeeded; no package version or permanent
audit policy changed, and no new online vulnerability audit is claimed. An
initial fixture compile caught a missing router namespace import, fixed before
the successful runs. The formatter's build-host pipe was denied inside the
sandbox; approved host execution completed scoped formatting/verification.
Tests used the direct xUnit runner rather than repeating the prior review's
VSTest testhost connection failure. The original handoff's aggregate suite,
coverage/API/dependency gates and known full-solution formatting limitation are
not newly repeated or reclassified here. Linux/live qualification was not run.

## Limits and next independent review

The [decision record's future-work table](model-qualification-bq1a-remediation-decisions.md#f1-and-future-work-ownership)
assigns ownership explicitly:

- BQ-1b: physical transport admission/counting, hidden retry audit, frozen
  subject/protocol binding, input/output/byte budgets, timeout/idle enforcement,
  bounded cleanup and unknown usage. These scenarios remain observers.
- BQ-2: evidence schema/projection, attribution/corrections and separate cohorts
  by contract/build/configuration. Historical successes are not upgraded.
- BQ-3/separate adapter work: F1 ordering defect and independently preserved
  wire/adapter provenance. The current normalized boundary cannot prove a
  provider emitted a Gemini or collector-assigned ID.
- BQ-4: complete ordered parts, signatures/continuations, sequential/parallel
  tool-result replay and repair-aware profiles. Turn-local correlation is not
  multi-turn replay qualification. Scalar conservation cannot detect content
  lost before normalization.

Independent re-review should challenge the new applicability/correlation split,
the strict repair-path composition rule, opaque-ID policy, missing versus failed
observations and outcome precedence. In particular, verify that provider
provenance never becomes Passed via `QualificationResultV3.Succeeded`, that
caller-controlled protocol binding cannot be mistaken for observed provider
support, and that F1 continues to be attributed to the adapter rather than model
variability. Inspect the exact eight-file implementation diff and rerun the
negative fixtures; do not infer acceptance from their passing test status.

**REMEDIATION COMPLETE, INDEPENDENT RE-REVIEW REQUIRED.** BQ-1a is not declared
accepted. There is no authorization to merge, change production adapters, start
BQ-1b, perform live qualification or publish packages.
