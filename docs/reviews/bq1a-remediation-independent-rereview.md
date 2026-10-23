# BQ-1a remediation technical re-review

Date: 2026-10-23 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Recommendation: REMEDIATION REQUIRED.** The original F2, F3 and F4 witnesses
are corrected in the new contracts. Two remaining issues prevent acceptance:
completion evidence can be erased by an unrelated diagnostic record, and the
unversioned Ollama profile asserts an unsupported absence of wire identity.
These require focused qualification-layer corrections, not a broader redesign.
Green tests alone do not establish acceptance.

## Review binding and provenance

| Role | Exact commit |
| --- | --- |
| Original design base | `355685a04931ec0d87ae3b74232b123e9d04d86e` |
| Original implementation | `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` |
| Original implementation handoff | `dab7c8f7efe3274e7285fc97bbc4f1d407633857` |
| Original review and 16 preserved probes | `b169d6ab66a211b611183d1d44e97f0419a20b06` |
| **Remediation source reviewed** | **`68cd12a8ca1a019f7b863db87a1f62e6c10eebdd`** |
| Checked-out handoff tip | `2898917f4dfefc476c184d281cb68c0c59513e53` |

Branch: `implementation/baize-bq1a`, local worktree `baize-bq1a`. The last tip
adds only `docs/model-qualification-bq1a-remediation-handoff.md` to the reviewed
remediation. All implementation locations below are identical at both commits.
The worktree was clean before this review. Only this report and its new probe
bundle were added; no implementation, production, original review or original
probe files were changed.

Reviewer: Codex. This chat previously performed the remediation. This pass
independently challenges its conclusions using new inputs, source inspection
and reruns, but it is **not an author-disjoint review**. That provenance must
remain attached to the decision; it is not an independent reviewer sign-off
from another person or agent.

Read the original review and executable probes, original qualification design,
remediation decisions, all eight files in the remediation diff and the handoff.
Inspected relevant production OpenAI/Ollama/Gemini mappings, collector,
assembler, router diagnostics, telemetry and repair behavior. Provider-shaped
executions below use in-memory `HttpMessageHandler` responses. No live provider
calls, package publication, merge or BQ-1b implementation occurred. Official
upstream protocol source was read to challenge applicability.

Severity: P2/medium denotes a concrete false acceptance or erroneous evidence
classification. Whether a defect predates the remediation is stated separately
from its effect on the current contracts.

## Confirmed findings requiring correction

### R1 — P2: null completion metadata erases explicit incomplete evidence

**Affected contracts:** `chat.baseline@3`, `tools.single@3`,
`chat.streaming-integrity@2`.

**Location:**
[QualificationScenariosV3.cs](../../tests/Penghou.Baize.Qualification.Contracts/QualificationScenariosV3.cs):301
unconditionally assigns `ProviderDone = evt.Diagnostics.Done`. That state gates
`execution.completed` at lines 82–84 and `stream.terminal` at lines 173–175.
[LlmProviderDiagnostics.cs](../../src/Penghou.Baize/LlmProviderDiagnostics.cs):7,24
defines the completion observation as nullable and defaults it to null.

**Counterexample:** complete the following public event stream normally, with
otherwise valid accounting and a single successful completion activity:

```text
Delta("BAIZE_OK")
FinishReason("stop")
Diagnostics(Done: false)
Diagnostics(Done: null)  // metadata without a completion observation
```

`R1-incomplete-then-unknown` returns `Succeeded=true`; both contracts are Passed,
and every assertion is Passed. Removing the last record produces the correct
Failed outcome (`R1-explicit-incomplete-control`). The same erasure also passes
`tools.single@3` with valid native arguments, and passes the routed baseline
with required diagnostics and one final successful attempt. The three witnesses
are [Program.cs](bq1a-remediation-rereview-probes/Program.cs):57–59 and recorded
in [results.jsonl](bq1a-remediation-rereview-probes/results.jsonl).

**Reasoning:** absence of a new completion observation cannot refute the known
false observation. Assigning null causes the later `!= false` checks to accept
contradictory completion evidence. The router snapshot and collector activity
do not establish that the provider completed. This is a retained observer defect
now present in the new versions, not a newly introduced adapter regression.
The counterexample is verified at the explicit public-stream seam; this review
does not assert a built-in provider normally emits this diagnostic trailer.

**Recommended disposition:** fix before accepting current qualification. Preserve
the latest meaningful completion observation rather than overwriting it with
missing data, and add permanent negative tests for both scenarios and routed
composition. Preserve the positive controls: intermediate `Done=false` followed
by explicit `Done=true` is valid, and complete followed by metadata-only is
valid. Both controls currently pass. Rejecting every intermediate false value
would introduce a new false negative.

### R2 — P2: Ollama applicability confuses absent adapter evidence with no protocol ID

**Affected evidence claim:** `ToolIdentityEvidence.ProviderIdentityApplicability`
and `ProviderIdentityCode` associated with `tools.single@3`. This finding does
not dispute native validity or turn-local canonical correlation of ID-less calls.

**Locations:**
[QualificationScenariosV3.cs](../../tests/Penghou.Baize.Qualification.Contracts/QualificationScenariosV3.cs):260–266
always classifies `OllamaChat` as NotApplicable / `protocol-has-no-wire-call-id`.
[QualificationProviderV3Tests.cs](../../tests/Penghou.Baize.Qualification.Tests/QualificationProviderV3Tests.cs):25–37
asserts that classification using only an ID-less fixture. The
[decision table and rationale](../model-qualification-bq1a-remediation-decisions.md)
at lines 109 and 121–123 do not restrict the claim to a server/protocol revision.

**Protocol evidence:** the official current Go wire type has an optional
`ToolCall.ID` serialized as `id,omitempty`, and `Message.ToolCallID`.
[Ollama API types, main](https://raw.githubusercontent.com/ollama/ollama/main/api/types.go)
(read 2026-10-23, `ToolCall` lines 207–210 in the retrieved source). In contrast,
the historical `v0.12.0` type includes only `Function` in `ToolCall`.
[Ollama API types, v0.12.0](https://raw.githubusercontent.com/ollama/ollama/v0.12.0/api/types.go)
(lines 172–174). Main is mutable; this is not a claim about an installed or
tested server version. It establishes why an unversioned universal absence
claim is unjustified. A no-ID example does not establish protocol-wide absence.

**Counterexample through the actual adapter:** provide this tool entry in an
otherwise valid in-memory Ollama chat stream, followed by `done:true` / stop:

```json
{"id":"provider_call_17","function":{"index":0,"name":"echo_value","arguments":{"value":"baize-live"}}}
```

`R2-real-ollama-wire-id-present`
([Program.cs](bq1a-remediation-rereview-probes/Program.cs):73–81) returns native
and streaming Passed, canonical origin CollectorAssigned, provider applicability
NotApplicable and code `protocol-has-no-wire-call-id`. The current
[OllamaToolCall.cs](../../src/Penghou.Baize.Ollama/OllamaToolCall.cs):9–16 has no
ID property, and
[OllamaChatClient.cs](../../src/Penghou.Baize.Ollama/OllamaChatClient.cs):330–341
always emits `Id:null`. Thus the fixture's wire identity is lost before the
qualification boundary; its absence there does not prove inapplicability.
The ID-less positive control also passes, as it should.

**Recommended disposition:** correct applicability before acceptance. For the
unversioned profile, use Applicable/NotObserved if adopting the optional-ID
protocol, or Undetermined until the profile is bound more precisely. Reserve
NotApplicable for an explicitly scoped, evidenced no-ID protocol revision.
Keep native ID-less tool validity and collector correlation available, and keep
provider provenance NotObserved at this boundary. Add ID-present and ID-absent
adapter fixtures and correct the decision record. Forwarding wire IDs in the
production adapter is separate work and is not necessary to fix this evidence
classification. Do not globally require OpenAI-style IDs.

## Verification of the original findings and design decisions

| Item | Verified result and disposition |
| --- | --- |
| F2: undeclared tool in baseline; answer accompanying the single tool | Closed for the original witnesses in V3. Both independently reject on `response.shape`; corresponding clean controls pass. Whitespace formatting remains allowed. Reasoning is a separate channel. |
| F3: zero or failed router attempts | Closed for the original witnesses. Both fail even with successful activity telemetry. A premature snapshot and failure followed by success also reject; the numeric snapshot list retains the failed observation. One final successful attempt passes. Supplied-but-optional diagnostics are still validated. |
| F4: foreign/bare cancellation invented as timeout | Closed. Independent foreign-token OCE is Inconclusive / `ambiguous-cancellation`, explicit fired deadline is Blocked / `timeout`, caller cancellation is Cancelled. The supplied tests also cover bare OCE, typed timeout, both tokens, preflight and disposal. |
| Incomplete observations and outcome precedence | Final-dependent checks are NotObserved before a final response. Generic HTTP 400 is Blocked, not unsupported. Observed shape violations survive as failed assertions under an operational disposition. Caller/deadline precedence is explicit; forced disposition must not erase the assertion list in a future consumer. R1 is a separate remaining completion-evidence defect. |
| Native validity / canonical correlation / provider provenance | Structurally distinct claims. Stable indexes/names/IDs and known part association are checked against collected calls. OpenAI and Anthropic require nonempty normalized IDs; optional-ID profiles permit collector fallback. Unknown tool protocol blocks before invocation. Provider status remains NotObserved even when `Succeeded=true`; no provider-ID pass is created. R2 corrects one applicability classification within this design. |
| Gemini | Actual adapter supplies `call_N` when the wire ID is absent (`GeminiChatClient.cs`:211–216). Independent real-adapter fixtures with and without IDs both pass native/canonical checks and retain Applicable/NotObserved provenance. NormalizedStream describes where the canonical ID was seen, not who created it. No false provider provenance pass observed. |
| Repair path | Explicit strict composition policy, not an assertion that repair changed bytes or that the model is defective. Sticky flags, nonempty attempts and detailed diagnostics prevent a native pass; later empty/default metadata cannot clear this state. Detailed-only and true-then-false independent witnesses reject. Validation-only/skipped repair paths are intentionally outside this native profile. |
| JSON uniqueness | Recursive decoded-name ordinal comparison rejects duplicate members, including nested objects/arrays, both orders, identical duplicates and escaped names in supplied tests. Independent escaped duplicate rejects. Invalid syntax does not invent a duplicate observation; uniqueness/schema/value remain NotObserved. This observes normalized arguments, not unseen wire bytes. |
| Opaque IDs | Removed the arbitrary 256-character limit and preserves exact comparison without trimming/format inference. Independent 257-character ID passes correlation with provenance NotObserved. Empty/conflicting IDs still fail. No raw IDs are persisted in results. Resource admission is future execution work. |
| Historical evidence | Original seven contract/fixture/test source blobs were compared with `b169d6a` and are identical. Original 16 probe outputs reproduce case- and order-sensitively, including old false acceptances. Old versions remain explicitly named `chat.baseline@2`, `tools.single@2`, `chat.streaming-integrity@1`; new versions are distinct. |
| Current callers | Both current integration wrappers explicitly call V3 and retain method names/traits. Search of integration tests, `eng` and `.github` found no old scenario entry point used for current qualification. No aliases or automatic conversion upgrade old successes. There is no current evidence projector; its future version gate remains required. |
| Production and dependencies | Remediation changes no `src/` files or `Directory.Build.props`; the new project references are test-only existing projects. This review changes none of those files. No new production capability flags or model claims were found. |

### F1 remains an exposed production adapter defect, outside BQ-1a remediation

**Severity/location:** original F1 remains open at
[OpenAiChatClient.cs](../../src/Penghou.Baize.OpenAi/OpenAiChatClient.cs):196–210,
247. The adapter emits a public finish before processing a tool delta from the
same wire choice. No production behavior changed in the remediation.

The new review independently feeds the real adapter identical valid tool data
with split versus co-terminal finish. Split passes; co-terminal fails the strict
sequence check. A payload and finish in the same public event passes, showing
that the contract does not reject co-terminal public events indiscriminately.
Supplied remediation tests additionally retain final-fragment and routed cases.
The causal distinction is adapter emission ordering, not tool schema or a model
refusing tools. A contract failure here cannot establish unsupported model
capability. The decision record explicitly excludes that inference; current
outcomes have no automatic Unsupported classification.

**Disposition:** preserve the regression and fix only in separately authorized
adapter work; BQ-3 can reference the quirk. BQ-2 attribution must preserve the
adapter cause when projecting evidence. Do not relax the strict sequence rule
to make this provider fixture green. F1 alone is not a new BQ-1a remediation
blocker.

## Accepted limitations and future work

These are bounded claims already disclosed by the remediation, not additional
acceptance blockers or requests to implement BQ-1b here.

| Category | Limit and appropriate owner |
| --- | --- |
| Accepted BQ-1a observation boundary | Activity/normalized accounting cannot detect content lost before normalization or establish provider identity. Current contracts must be described at that boundary. Wire provenance needs separate BQ-1b binding / BQ-3 evidence. |
| Future BQ-1b execution | One stream invocation, one activity and coherent router diagnostics do not prove one physical HTTP attempt or account for hidden retries. Admission, subject/protocol binding, byte/token/spend ceilings, idle/deadline enforcement and bounded cleanup belong at the execution boundary. |
| Future BQ-2 projection | Keep exact contract/build/configuration cohorts; never flatten `Succeeded` into provider-ID verification or remote-model fault. Old V2 types are still callable historical code, not an enforced application-wide allowlist. Current wrappers are safe; a new projector must explicitly gate supported versions and preserve raw assertions/attribution. |
| Future BQ-4 fidelity | Flat text/reasoning/arguments and known tool-part consistency do not qualify complete ordered parts, signatures, continuation or multi-turn tool-result replay. Full sequential/parallel/continuation scenarios remain future work. |
| Design concern, bounded by current scope | The caller-selected protocol is trusted composition metadata, not detected provider support. BQ-1b must freeze the real adapter/protocol/revision with the subject. Strict repair-path composition must retain its explicit label; repair-aware profiles are separate BQ-4 work. |

## Negative-test adequacy

The added suite meaningfully covers the original corrections: assertion-level
shape checks, multiple contradictory router snapshots, cancellation sources,
NotObserved semantics, recursive duplicate members, repair metadata variants,
opaque IDs, protocol preflight and actual adapter fixtures. It also protects
required IDs and the known OpenAI defect instead of hiding them.

However, it does not cover the completion-observation-to-null transition (R1).
Its Ollama fixture has no optional wire ID and asserts the blanket applicability
conclusion (R2). Test count and green status cannot resolve either issue. The
new review bundle adds these inputs with positive controls; it deliberately
records the defective actual outcomes rather than changing the implementation
or rewriting the old evidence.

## Commands and observed results

All commands ran locally from the repository root using cached dependencies.
Qualification/integration builds completed with zero warnings/errors. Direct
xUnit in-process runners were used; the earlier testhost connection limitation
was not bypassed by enabling network access or live tests.

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1a_rereview_no_environment_file__'

dotnet build tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo

dotnet build tests/Penghou.Baize.IntegrationTests/Penghou.Baize.IntegrationTests.csproj --configuration Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -trait 'Capability=Baseline' -trait 'Capability=Tools'
dotnet tests/Penghou.Baize.IntegrationTests/bin/Release/net10.0/Penghou.Baize.IntegrationTests.dll -noLogo -list tests -trait 'Capability=Baseline' -trait 'Capability=Tools'

dotnet build docs/reviews/bq1a-independent-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
$actual = @(dotnet docs/reviews/bq1a-independent-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll)
$expected = @(Get-Content docs/reviews/bq1a-independent-probes/results.jsonl)
if ($LASTEXITCODE -ne 0 -or $actual.Count -ne 16 -or $expected.Count -ne 16 -or
    [string]::Join([Environment]::NewLine, $actual) -cne [string]::Join([Environment]::NewLine, $expected)) { throw 'Original probe replay differs' }

dotnet restore docs/reviews/bq1a-remediation-rereview-probes/ReviewProbes.csproj --configfile docs/reviews/bq1a-remediation-rereview-probes/NuGet.Config --verbosity quiet -m:1
dotnet build docs/reviews/bq1a-remediation-rereview-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet docs/reviews/bq1a-remediation-rereview-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll > docs/reviews/bq1a-remediation-rereview-probes/results.jsonl
```

| Check | Observed result |
| --- | --- |
| Existing qualification suite | **138 passed**, 0 failed/errors/skipped |
| Original independent probes against frozen versions | **16/16 outputs identical**, case/order-sensitive |
| New challenge probes | **33 outputs**, replay completed; R1 has three false acceptances, R2 reproduces incorrect applicability. Other controls and counterexamples behave as described above. |
| Integration opt-out | Exactly 2 selected tests, both skipped by the live-test gate; 0 failures/errors |
| Integration discovery | Exactly the two preserved baseline/tool wrapper method names discovered |
| Scope and preservation | No production/dependency-policy diff in remediation; historical blobs unchanged; current wrapper calls use V3; no implementation edits during review |

The new probe executable is an observation recorder, **not a passing-test gate**:
exit 0 means all cases ran, not that all expected properties held. Its output is
safe numeric/status evidence, with raw IDs omitted. `NuGet.Config` clears package
sources; the probe links the already built reviewed assemblies and unchanged
`ScenarioFixture.cs`. Build those assemblies from the bound commit first.
Ignored `bin/` and `obj/` files are not review artifacts. No full production
suite, online vulnerability audit, Linux run or live qualification is claimed.

## Final disposition

**REMEDIATION REQUIRED**, limited to R1 and R2 plus their regression tests and
explicit decision/evidence updates. F2/F3/F4's original witnesses are closed in
the new versions; F1 remains a separately attributed adapter defect. Preserve
the original V2 evidence and this exact-commit record when making corrections.
Re-review the targeted correction rather than reopening unrelated future work.
This decision grants no merge, publication, paid qualification or BQ-1b
authorization.
