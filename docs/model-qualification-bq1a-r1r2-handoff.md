# BQ-1a R1/R2 final remediation handoff

Date: 2026-10-24 (Asia/Manila). Repository:
`jenolaszlo-sketch/penghou-baize`, branch `implementation/baize-bq1a`.

**R1/R2 REMEDIATION COMPLETE - INDEPENDENT VERIFICATION REQUIRED**

This handoff covers the two remaining findings in the
[technical re-review](reviews/bq1a-remediation-independent-rereview.md).
It does not declare BQ-1a accepted. F2/F3/F4, repair/uniqueness/opaque-ID policy
and other accepted boundaries have not been reopened. F1 remains a production
OpenAI adapter defect; its behavior and attribution are unchanged.

## Exact Git and evidence bindings

| Role | Exact commit |
| --- | --- |
| Original design base | `355685a04931ec0d87ae3b74232b123e9d04d86e` |
| Original implementation | `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` |
| Original handoff | `dab7c8f7efe3274e7285fc97bbc4f1d407633857` |
| Original review and 16 probe outputs | `b169d6ab66a211b611183d1d44e97f0419a20b06` |
| Reviewed V3 implementation before R1/R2 | `68cd12a8ca1a019f7b863db87a1f62e6c10eebdd` |
| Previous remediation handoff | `2898917f4dfefc476c184d281cb68c0c59513e53` |
| R1/R2 re-review and 33 original challenge outputs / starting tip | `2a5ab3efc9155fad30a88511acde7a4d6fbfa61a` |
| **Corrected implementation and permanent regressions** | **`123dd86249eba57be700d5e32b486a7527e06474`** |

The corrected implementation commit contains exactly five files: the V3
observer, the new completion regression class, the existing V3 provider and
scenario tests, and the updated decision record. This handoff and the new replay
artifacts follow in a documentation/evidence commit. No code changes follow
the tested implementation commit.

The [new evidence directory](reviews/bq1a-r1r2-remediation-evidence/) contains
`challenge-results.jsonl`, a binding manifest and `Verify-ChallengeReplay.ps1`.
The original 33-case probe **source** is replayed unchanged; its old outputs
remain at their existing path and commit. The manifest binds the new results to
the corrected implementation and records the source blob and output hash
(UTF-8 without BOM, CRLF normalized to LF).

## Corrections and regression coverage

**R1:** `QualificationScenariosV3.Observation` now retains the last non-null
provider `Done` observation. Unknown starts null; false is incomplete; true is
complete. Null does not clear either known value. Later explicit values replace
earlier ones in either direction, so ordinary intermediate false followed by
true passes, while true followed by explicit false remains incomplete until
another explicit true. Existing unknown-completion behavior remains unchanged:
missing provider flags are not a claim that the provider reported complete.

[QualificationCompletionV3Tests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationCompletionV3Tests.cs)
has 36 deterministic cases: nine sequences across baseline/native-tool and
direct/routed public-event compositions. These promote all independent R1
counterexamples and controls, and add unknown-only, no-diagnostics and later
explicit replacement cases. Assertions distinguish completion rejection from
schema/reconstruction/router failure, and preserve disposal and single invocation.

Eight additional cases in
[QualificationProviderV3Tests.cs](../tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs)
compose the actual router/OpenAI adapter over an in-memory handler with explicit
test diagnostic observations, for both baseline and native tools. The injected
final observations precede the router's final successful snapshot and follow
the adapter's own completion report. They establish that router success cannot
erase incomplete provider evidence. They do not claim OpenAI emits the injected
diagnostics or prove physical transport admission.

**R2:** the unversioned `OllamaChat` profile now reports applicability
`Undetermined`, code `protocol-revision-unbound`. Provider identity provenance
remains `NotObserved`. No version-binding API or production behavior changed.
Two real-Ollama-adapter fixtures, with and without an optional fake wire ID,
both validate the native tool and canonical correlation successfully. Both
report `CollectorAssigned` canonical origin because the unchanged adapter emits
no normalized ID. Neither an ID-bearing fixture nor the collector's assigned
identity becomes a provider-provenance pass. The ID itself is not stored in the
result. Required normalized IDs for OpenAI/Anthropic and Gemini's optional-ID
behavior remain strict and unchanged.

The [updated decision record](model-qualification-bq1a-remediation-decisions.md#r2-version-scoped-ollama-evidence-and-boundary-limits)
contrasts the historical v0.12.0 wire type with the optional ID in mutable main,
and explicitly limits conclusions to the inspected revision/source. No actual
server revision is established by the qualification context. Wire-ID forwarding
is separate production adapter work, outside this correction.

## Version disposition and preserved evidence

Keep the unaccepted identifiers `chat.baseline@3`, `tools.single@3` and
`chat.streaming-integrity@2`, **bound to the exact implementation above**.
The repository records only deterministic V3 fixtures and reviews, all explicitly
unaccepted. A local containing-ref/tag check finds only this branch for `68cd12a`,
with no containing remote-tracking branch/release tag. No external publication
or consumer audit is claimed. No accepted/published V3 model qualification claim
was found in the available evidence. If external qualification usage is found,
isolate that cohort and evaluate a new contract version before publishing or
consuming corrected evidence. A contract ID alone cannot identify these builds.

The original seven contract/fixture/test source blobs match `b169d6a`; all eleven
existing review/probe files match `2a5ab3e`. This includes both original result
sets, source projects and reviews. Previous handoffs remain unchanged. The new
33-case replay is separate evidence and does not rewrite or upgrade a previous
success. The test request fixtures, contract descriptors and integration
wrapper sources are unchanged by R1/R2. No production files, APIs, dependencies,
package versions or capability declarations changed.

## Verification commands and exact results

Run from the local Baize worktree root. Builds use available cached dependencies;
no restore, live credential access or provider endpoint is needed. All provider
fixtures return data through custom in-memory handlers; fake `.invalid` URLs
never reach sockets. The direct xUnit in-process runner avoids the previously
documented VSTest connection issue.

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1a_r1r2_no_environment_file__'

dotnet build tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo
dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo -method '*R1_*' -method '*R2_*'

dotnet build docs/reviews/bq1a-independent-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
$actual = @(dotnet docs/reviews/bq1a-independent-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll)
$expected = @(Get-Content docs/reviews/bq1a-independent-probes/results.jsonl)
if ($LASTEXITCODE -ne 0 -or $actual.Count -ne 16 -or
    [string]::Join([Environment]::NewLine, $actual) -cne [string]::Join([Environment]::NewLine, $expected)) { throw 'Historical replay differs' }

dotnet build docs/reviews/bq1a-remediation-rereview-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
# Use a fresh file, preserving both recorded result cohorts.
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
dotnet docs/reviews/bq1a-remediation-rereview-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll > artifacts/bq1a-r1r2-replay.jsonl
./docs/reviews/bq1a-r1r2-remediation-evidence/Verify-ChallengeReplay.ps1 -ReplayPath artifacts/bq1a-r1r2-replay.jsonl

dotnet build tests/Penghou.Baize.IntegrationTests/Penghou.Baize.IntegrationTests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -trait 'Capability=Baseline' -trait 'Capability=Tools'
dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -list tests -trait 'Capability=Baseline' -trait 'Capability=Tools'

dotnet format whitespace . --folder --include tests/Penghou.Baize.Qualification.Contracts/QualificationScenariosV3.cs tests/Penghou.Baize.Qualification.Tests/QualificationCompletionV3Tests.cs tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs tests/Penghou.Baize.Qualification.Tests/QualificationScenarioV3Tests.cs --verify-no-changes --verbosity quiet
git diff --check
```

The recorded challenge command wrote the new directory's
`challenge-results.jsonl`; the example above creates `artifacts/` for a
subsequent replay. The committed checker also validates the
recorded file by default. Build the reviewed assemblies first. Probe programs
are observation recorders; exit 0 alone is not an acceptance gate.

| Check | Result |
| --- | --- |
| Full deterministic qualification suite | **183 total, 183 passed, 0 failed/errors/skipped** |
| Focused R1/R2 selection | **46 total, 46 passed, 0 failed/errors/skipped**; included in the 183 |
| Historical original probes | **16/16 outputs identical**, case/order-sensitive |
| Latest challenge source replay | **33/33 records verified** against the explicit expected transformation; 28 records unchanged |
| R1 challenge change | Exactly three false passes now fail both contracts on `execution.completed` and `stream.terminal`; positive controls and incomplete control remain correct |
| R2 challenge change | Exactly two Ollama records change only applicability/code; native/canonical pass and provenance NotObserved remain unchanged |
| F1 | Existing four whole/final-fragment, direct/routed regression cases pass by asserting the known sequence failure; challenge co-terminal failure and split control unchanged |
| Integration opt-out | **2 total, 2 skipped, 0 failed/errors**, explicitly due to the live-test gate |
| Integration discovery | Exactly `NativeToolSmokeTest_ReturnsSchemaValidArguments` and `StreamingSmokeTest_ReturnsExpectedContentAndDiagnostics` |
| Builds | Qualification, integration and both probe builds: **0 warnings, 0 errors** |
| Formatting and scope | Scoped whitespace check and `git diff --check` pass; no production/dependency diff; preserved blobs identical |

A pre-fix focused run produced 14 failures out of 46, exposing the old nullable
assignment and applicability. During test development an initial real-router
fixture inserted its observations before the adapter's later explicit true;
the first post-fix suite consequently had four fixture expectation failures.
The fixture was corrected to insert after that explicit completion observation,
before the router snapshot. The final 183/46 results above use the corrected
fixture. No production fix or contract weakening was used to resolve it.

No full production regression/API/dependency audit, online vulnerability audit,
Linux run, paid qualification or live capability result is claimed. The change
is confined to the deterministic qualification layer and its evidence.

## Remaining boundaries and verification gate

- F1 remains separately authorized adapter/BQ-3 quirk work. Its signature must
  not become an unsupported-model inference or be concealed by relaxing order.
- Physical transport admission/hidden retry accounting, protocol/subject binding
  and bounded execution remain BQ-1b work. No runner was added.
- Wire identity provenance and Ollama ID forwarding remain unobserved/separate
  adapter work. Canonical correlation is not provider provenance.
- Historical/current cohort projection and attribution remain BQ-2 work; full
  ordered parts, signatures, continuations and multi-turn fidelity remain BQ-4.

Another focused verification should reproduce the three corrected R1 challenge
outcomes and both positive controls, confirm both Ollama fixtures remain
successful without provenance claims, and confirm the 28 unchanged challenge
records and frozen historical evidence. This chat authored the changes and
cannot provide an author-disjoint acceptance sign-off. No merge, publication or
BQ-1b authorization follows from these test results.

**R1/R2 REMEDIATION COMPLETE - INDEPENDENT VERIFICATION REQUIRED**
