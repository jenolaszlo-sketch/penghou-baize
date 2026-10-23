# BQ-1a focused independent technical review

Date: 2026-10-23 (Asia/Manila). Reviewer: Codex, reviewing the committed implementation independently of its handoff conclusions.

**Recommendation: REMEDIATION REQUIRED.** The extraction is useful and its supplied negative tests pass, but it can accept contradictory execution evidence and responses that violate its fixtures. An ambiguous cancellation receives an invented timeout classification. Native identity also needs an explicit protocol-aware design decision. The documented OpenAI co-terminal failure is a real adapter ordering defect; relaxing the stream contract would conceal it.

This recommendation concerns qualification evidence. It neither authorizes merging nor authorizes BQ-1b, live qualification, adapter changes or package publication. No production code or existing tests were edited during this review. All executed provider-shaped inputs used in-memory HTTP handlers; there were no live provider calls.

## Exact review binding and scope

Repository: `jenolaszlo-sketch/penghou-baize`.

| Role | Exact commit |
| --- | --- |
| Design base | `355685a04931ec0d87ae3b74232b123e9d04d86e` |
| Implementation reviewed | `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` |
| Handoff tip / checked-out source reviewed | `dab7c8f7efe3274e7285fc97bbc4f1d407633857` |

The handoff tip adds only `docs/model-qualification-bq1a-handoff.md` to the implementation. Source locations below refer to that exact tip and are identical at the implementation commit. The initial working tree was clean. This review adds documentation and a standalone reproduction bundle under `docs/reviews/`; it does not update the reviewed commits or assert that remediation has occurred.

Read the implementation diff, all new contract/test files, both changed integration wrappers and factory changes, the handoff, and the qualification architecture, decisions, evidence, execution, quirks, workflow and NVIDIA design documents. Traced the collector, core assembler/telemetry, router and relevant OpenAI, Gemini and Ollama mappings. Existing production behavior is distinguished from defects introduced by the qualification checks.

Severity: **P1/high** can systematically misrepresent an affected capability or needs resolution before affected qualification use; **P2/medium** can accept defective evidence or misclassify an execution under a concrete counterexample. Severity is independent of whether the fault is newly introduced. Findings remain open; recommendations are proposed actions, not applied changes.

## Findings requiring disposition

### F1 — P1: OpenAI co-terminal tools are reordered after the public finish event

**Classification:** Verified pre-existing adapter defect, correctly exposed by the new contract. This is not evidence of invalid model arguments or an unsupported provider capability.

**Affected contracts:** `chat.streaming-integrity@1/stream.sequence` and `tools.single@2/execution.completed`.

**Exact locations:** [OpenAiChatClient.cs:197-208 and 211-255](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/src/Penghou.Baize.OpenAi/OpenAiChatClient.cs#L197-L255); detection at [QualificationScenarios.cs:95-98, 181 and 245](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationScenarios.cs#L95-L98); supplied regression at [ProviderFixtureTests.cs:58-74](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Tests/ProviderFixtureTests.cs#L58-L74).

**Evidence/counterexample:** A wire choice containing both a complete `echo_value` tool delta (`call_1`, `{"value":"baize-live"}`) and `finish_reason: "tool_calls"`, followed by `[DONE]`, produces a finish event before the tool event. Independent probe `real-openai-coterminal` returns both contracts `Failed`, while the name, identity, JSON, schema, value, terminal count, reconstruction and accounting checks pass. The failing checks are precisely `execution.completed` and `stream.sequence`. `Diagnostics.FailureCode` is null because enumeration itself succeeded. The supplied separate-terminal control passes. A single public event containing the final tool delta and finish passes (`late-tool-at-same-terminal`), establishing that the contract already permits payload co-terminal with finish; it rejects the adapter's reordering across events.

The [OpenAI streaming schema](https://developers.openai.com/api/reference/resources/chat/subresources/completions/streaming-events) defines delta and finish reason on the same choice, with tool calls in the delta. The adapter must preserve final payload before, or together with, its finish. The defect's method is `ProcessStreamAsync`; the handoff calls it `StreamCoreAsync`, which is not the source location here.

**Recommended action:** Keep the sequence assertion. Track this as a fixture-confirmed adapter defect, and resolve through separately authorized adapter work or explicitly restrict affected qualification claims until resolution. Add a regression for a previously opened tool whose **last argument fragment** shares the finish chunk, and one through the routed composition. The current whole-call fixture is valuable but asserts the known failure; its passing xUnit status does not close that failure. Do not add a blanket exception allowing arbitrary post-finish tool payload, and do not project this signature as model failure/Unsupported.

**Disposition:** OPEN, confirmed adapter finding. Separate adapter remediation is outside this review. This finding alone does not establish that the extraction logic is wrong.

### F2 — P2: Semantic checks accept output shapes prohibited by their fixtures

**Classification:** Verified qualification assertion gap / false acceptance.

**Affected contracts:** `chat.baseline@2` and `tools.single@2`; both can be returned `Passed` together with streaming integrity.

**Exact locations:** [QualificationScenarios.cs:83-89 and 153-162](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationScenarios.cs#L83-L89); fixture requirements at [QualificationFixtures.cs:20-28](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationFixtures.cs#L20-L28).

**Evidence/counterexamples:**

- `tool-with-unrequested-answer`: ordinary content `I refuse to call the tool.`, followed by the correct native call and `stop`, returns `Succeeded=true`. The fixture expressly says not to answer in text, yet `ToolAssertionsAsync` never checks content.
- `baseline-with-undeclared-tool`: `BAIZE_OK`, a well-formed `echo_value` call, then `stop`, also returns `Succeeded=true`, even though the baseline request declares no tools. Exact comparison of the text channel does not validate the entire response shape.

Accounting and reconstruction correctly conserve these extra outputs, so the integrity contract cannot substitute for scenario-specific semantic checks. This is about ordinary answer text and undeclared tool calls; reasoning content is a separate channel and should not be forbidden merely because these fixtures allow thinking-capable models.

**Recommended action:** Add explicit, versioned expected-response-shape checks: no native calls for the baseline, and no substantive ordinary answer content for this tool-only fixture. Choose and document treatment of whitespace-only answer deltas. Add the two counterexamples as negative tests. Update descriptors and contract/version documentation through review; do not silently reinterpret persisted results.

**Disposition:** OPEN; remediate shared assertions before treating these scenarios as complete fixture-compliance evidence.

### F3 — P2: Contradictory router diagnostics satisfy execution and dispatch checks

**Classification:** Verified qualification assertion gap / false acceptance at the explicit-context seam. No claim that today's real router produces these contradictory success traces.

**Affected contracts:** All three; particularly `diagnostics.router`, `execution.completed` and `stream.dispatches`.

**Exact locations:** [QualificationScenarios.cs:95-99, 194 and 262](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationScenarios.cs#L95-L99). Only the maximum observed attempt-list count is retained; attempt outcomes are discarded.

**Evidence/counterexamples:** With `RequireRouterDiagnostics=true`, a correct baseline, valid completion telemetry and either (a) `LlmRouterDiagnostics([])` or (b) one attempt whose outcome is `Failed`, both return `Succeeded=true`. See `zero-router-attempts` and `failed-router-attempt`. A non-null count satisfies presence; zero or one satisfies the upper bound. Neither condition establishes that a routed stream succeeded on its single allowed attempt.

**Recommended action:** For a successful scenario requiring router diagnostics, require one completed successful attempt and reject contradictory terminal diagnostics. Preserve safe success/failure counts or enums so consumers can audit that conclusion without retaining endpoint IDs or messages. For blocked pre-dispatch executions, preserve zero observed attempts without manufacturing a successful dispatch assertion. Add negative tests for zero attempts, a failed sole attempt and inconsistent successive diagnostics, alongside the existing multi-attempt test.

This remains an observation check. Even corrected router diagnostics cannot prove the physical HTTP dispatch count; transport counting/admission stays the caller's BQ-1b responsibility.

**Disposition:** OPEN; required qualification-check remediation.

### F4 — P2: An unidentified cancellation is classified as a proven timeout

**Classification:** Verified outcome-classification defect against the design's explicit ambiguity rule.

**Affected contracts:** Both contracts produced by either scenario, and `QualificationDiagnostics.FailureCode`.

**Exact locations:** [QualificationScenarios.cs:52-55 and 102-106](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationScenarios.cs#L52-L55); the context has only one cancellation parameter and no known-deadline observation. [Evidence design: outcome attribution](https://github.com/jenolaszlo-sketch/penghou-baize/blob/355685a04931ec0d87ae3b74232b123e9d04d86e/docs/model-qualification-evidence.md#attempt-outcomes-state-and-attribution) requires caller/deadline distinction and leaves ambiguous cancellation inconclusive.

**Evidence/counterexample:** `foreign-cancellation` throws `OperationCanceledException` carrying an unrelated cancelled token while the supplied caller token is not cancelled. There is no expired deadline in the scenario. Both contracts nevertheless become `Blocked` with `FailureCode="timeout"`. An internal cancellation, delegate-specific cancellation or unknown source is thereby reported as a timeout without evidence. Caller cancellation already has a correct explicit path and should retain it.

**Recommended action:** Recognize timeout only when supported by an explicit deadline signal or a typed, trusted timeout observation. Return `Inconclusive` with a stable ambiguous-cancellation code when neither caller cancellation nor timeout is established. Add tests for the foreign-token case, a bare cancellation exception, caller cancellation and a known timeout. Define the future runner/context mapping before BQ-1b reuse; a runner should not need to reverse-engineer and overwrite an already asserted timeout.

**Disposition:** OPEN; required classification remediation.

### D1 — P1 design concern: Native tool support is conflated with wire call-ID availability

**Classification:** Verified rejection of a valid provider tool shape, caused by the contract/design identity policy rather than failure to implement that policy. A design decision is required; this is not permission to weaken identity checks.

**Affected contracts:** `tools.single@2/tool.identity`, `chat.streaming-integrity@1/stream.tool-deltas` and `stream.reconstruction`.

**Exact locations:** [QualificationScenarios.cs:157, 168-176 and 198-199](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/tests/Penghou.Baize.Qualification.Contracts/QualificationScenarios.cs#L157); [OllamaChatClient.cs:331-340](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/src/Penghou.Baize.Ollama/OllamaChatClient.cs#L331-L340) emits `Id: null`; [LlmStreamingExtensions.cs:300-305](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/src/Penghou.Baize/LlmStreamingExtensions.cs#L300-L305) supplies a collector ID. Compare Gemini's adapter synthesis at [GeminiChatClient.cs:210-214](https://github.com/jenolaszlo-sketch/penghou-baize/blob/dab7c8f7efe3274e7285fc97bbc4f1d407633857/src/Penghou.Baize.Gemini/GeminiChatClient.cs#L210-L214).

**Evidence/counterexample:** `real-ollama-native-tool-without-id` replays one correct function call and a normal done/stop response through the real Ollama adapter. It fails identity, tool-delta consistency and reconstruction, although schema, expected value, completion, terminal and accounting pass. Ollama's existing `StreamAsync_MapsNativeToolCallAndUsage` fixture uses the same no-ID protocol shape. The [official Ollama tool example](https://docs.ollama.com/capabilities/tool-calling) binds the function by index/name and returns tool results using `tool_name`, without a call ID.

Conversely, the Gemini adapter can synthesize `call_0` before the observer and satisfy this identity assertion. Thus two native protocols receive different qualification treatment depending on **where Baize synthesizes an ID**. The handoff acknowledges provenance limitations, but does not resolve this systematic asymmetry. A strict wire-ID requirement can be useful; it is narrower than native tool calling.

**Recommended action:** Explicitly separate native tool-output validity, stable canonical correlation identity and provider-emitted identity provenance. Review applicability per protocol; a protocol without wire IDs must not receive a model-output failure merely for that omission. If the existing strict contract is intentionally retained, name and scope that narrower claim and record the native-tool coverage gap. Add real-adapter fake-HTTP cases for Ollama and Gemini. Do not synthesize IDs in the scenario to manufacture native provenance, or silently bless any random collector fallback.

**Disposition:** OPEN design decision, required before general cross-provider capability claims. The current implementation follows the reviewed design's strict-ID direction, so this is not categorized as an accidental assertion regression.

## Additional concerns and limits

These observations are distinguished from the required defects above.

1. **Duplicate JSON members — policy concern (P2), not established deviation from the adopted validator.** At `QualificationScenarios.cs:123-128`, `TryGetProperty` chooses the last matching property. `duplicate-conflicting-value`, with `{"value":"WRONG","value":"baize-live"}`, passes all checks. The existing structural validator validates both strings against the string schema; neither it nor this fixture declares a duplicate-member policy. This produces ambiguous evidence for consumers with different JSON member handling. Decide whether qualification requires unique members, add both property orders as counterexamples and preserve existing schema-validator reuse. Do not call this a missing JSON Schema keyword implementation.

2. **Reconstruction is limited to flattened fields — reuse limitation (P2).** `QualificationScenarios.cs:170-176, 246 and 249-258` does not compare `LlmResponse.Parts`, continuation/signature metadata or stable part-index association. `different-part-index-for-tool-fragments` moves the same tool from `PartIndex=0` to `PartIndex=1` and still passes. The collector finds an existing tool by tool index and ignores its later part-index argument. This demonstrates a blind spot, not corruption of the scalar strings this contract currently compares. A pass must not certify multi-turn replay or ordered-part/continuation preservation. Extend with independently expected part/continuation fixtures before those BQ-4 contracts reuse it.

3. **Repair metadata coverage — optional hardening (P2).** `QualificationScenarios.cs:264` observes `ContentWasRepaired` and nonempty `ContentRepairAttempts`, but ignores `ContentRepairDiagnostics`. `repair-diagnostics-only` supplies `IsRepairAccepted=true` and `SucceededBy="repair"` while leaving the other fields empty; `tool.unrepaired` passes. This is a demonstrated contradictory-metadata acceptance at the delegate seam. No current standard decorator path producing that combination was established, and repair acceptance alone need not mean text changed. Define coherent provenance rules and test explicit corrections or a reported successful repair with missing companion flags; do not claim the probe proves a current production repair leak. The scenarios themselves do not invoke repair.

4. **Opaque-ID restrictions — design concern (P2).** `ValidIdentity` additionally rejects IDs longer than 256 characters or containing whitespace/control characters. `opaque-id-over-256` demonstrates rejection of an otherwise correct public stream. No cross-provider 256-character rule is established by the design. Document and justify this as a canonical Baize constraint, or version/scope it appropriately; do not mistake a storage/privacy convention for remote capability evidence. No live provider emitting such an ID was demonstrated.

5. **Unobserved versus failed assertions — outcome/reuse concern (P2).** A generic provider 400 or an untyped exception before the first event yields `Failed` for both contracts and failed empty-content/finish/reconstruction assertions (`generic-provider-400`, `untyped-execution-error`). The code retains safe `invalid-request`/`execution-error` codes, so this is not a demonstrated Unsupported projection or a concealed exception. However, no model content was observed. Define stage/attribution and `NotObserved` handling before projection; generic 400/404 or an internal exception cannot establish invalid model output. Preserve conclusive failures already observed before any later blocker, while distinguishing incomplete attempts.

6. **Accepted scope limits.** Trace-scoped Activity filtering, locking and disposal provide reasonable invocation isolation; the supplied concurrent test passes and preserves the ambient parent. Telemetry is an observation surface, not an independent wire oracle. Normalized character conservation cannot detect payload already lost during adapter decoding. One delegate invocation, one completion event and a router upper bound do not enforce one physical request. The wrappers configure `MaximumAttempts=1`, and the real fake-HTTP router test checks one HTTP call on 503. Hard transport admission, hostile/custom delegates, timeout/idle bounds and budget settlement remain BQ-1b work. An unknown cap-support state must remain unknown; serializing 512 does not prove a provider honored it.

## Tests, compatibility and missing counterexamples

The independent probes are preserved in [bq1a-independent-probes](bq1a-independent-probes/Program.cs), with [actual structured results](bq1a-independent-probes/results.jsonl). They exercise the unchanged contracts using the original deterministic fixture and real adapters with an in-memory handler. They are a standalone review artifact, outside both solutions, not production remediation or an added passing acceptance gate. The program prints actual behavior, including undesired passes, rather than asserting those passes are correct.

| Validation | Independently observed result |
| --- | --- |
| Focused contract build, Release, no restore, single MSBuild node, workload resolver disabled | Build completed before the VSTest connection failure below |
| Direct xUnit v3 in-process execution of the newly built qualification assembly | **47 total, 47 passed, 0 failed/skipped**, exit 0 |
| Standalone review probe build | **0 warnings/errors**, exit 0 |
| Review probe execution | **16 inputs**, exit 0; actual dispositions/checks retained in JSONL |
| Saved probe bundle rebuilt and replayed from `docs/reviews/` | **All 16 results reproduced byte-for-byte**, exit 0 |
| `git diff --check` before review artifacts | Passed |
| Integration declarations/traits/opt-in and factory diff | Statically checked: original public entry points and capability filters remain, only the two extracted wrappers request single dispatch |
| Fresh integration-project rebuild and subsequent opt-out execution | **Build: 0 warnings/errors. Exactly 2 matching tests, both skipped under live opt-out**, exit 0 |

The normal `dotnet test` route initially failed during host workload/MSBuild setup; disabling the workload resolver and using one build node allowed compilation. VSTest then failed to connect to testhost after 90 seconds. Direct xUnit execution avoided that host connection and ran all 47 tests successfully. Later the restricted Windows launcher became unavailable; an approved local-launcher retry of the integration rebuild exposed the full disk. Only this review's own scratch `bin`/`obj` outputs were removed. Once free space recovered, the integration rebuild and the two matching opt-out tests completed successfully, as did a rebuild/replay of the saved probe bundle. These transient environment failures did not establish production regressions. The handoff's full-solution counts remain handoff evidence; this review does not claim to have independently repeated the aggregate suite or full formatting/dependency gates.

The supplied suite has meaningful adversarial tests: exact text, missing/duplicate/late terminals, late error, malformed/extra/wrong-type tool arguments, conflicting IDs, missing telemetry, accounting/warnings, repair flags, cancellation and concurrent traces. However:

- Its sole native-tool real-adapter positive path is OpenAI with an earlier tool chunk and a separate finish. It misses cross-protocol ID semantics and final-fragment co-terminal variants.
- Its router test checks a valid successful count and a genuine 503; it misses contradictory zero/failed-success diagnostics.
- Its output negatives test wrong or extra text **within baseline text**, not extra output channels or tool-only text.
- Its cancellation coverage proves caller cancellation, not source attribution for an unrelated cancellation exception.
- Its repair negatives cover the simple flag, not contradictory provenance; reconstruction tests do not cover part/continuation fidelity.

For local reproduction, first build the qualification test project and the Ollama project at the exact reviewed revision, using the restored dependencies, then build/run the review project. Its sources use the existing test friend-assembly identity in a separate output directory; no friend-access production edit is needed. Example commands, from repository root:

```powershell
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1a_review_no_environment_file__'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet tests/Penghou.Baize.Qualification.Tests/bin/Release/net10.0/Penghou.Baize.Qualification.Tests.dll -noLogo
dotnet build src/Penghou.Baize.Ollama/Penghou.Baize.Ollama.csproj -c Release --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet restore docs/reviews/bq1a-independent-probes/ReviewProbes.csproj --configfile docs/reviews/bq1a-independent-probes/NuGet.Config --verbosity quiet
dotnet build docs/reviews/bq1a-independent-probes/ReviewProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet docs/reviews/bq1a-independent-probes/bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll
```

The initial execution used the same probe sources in a sibling scratch directory before preserving them here; project-relative include paths were adjusted for this documentation location. The relocated bundle was then rebuilt and all 16 structured results reproduced exactly. The initial Ollama replay used the pre-existing Release adapter assembly stamped with design commit `355685a04931ec0d87ae3b74232b123e9d04d86e`, whose production source is unchanged through the handoff. The final integration build rebuilt its dependencies, and the saved-bundle replay again produced identical results. This is deterministic adapter/contract evidence, not a live model observation.

## Disposition and closure conditions

**REMEDIATION REQUIRED** for reliable qualification use. Close F2-F4 with reviewed shared-contract changes and meaningful negative tests. Resolve D1 through an explicit identity/provenance/applicability decision and cross-adapter fixtures. Give F1 an explicit adapter-defect disposition and retain its strict regression; do not convert it into accepted ordering or model-negative evidence. Record choices for the additional policy concerns and preserve narrow claim boundaries for unresolved limits.

BQ-1b can mechanically consume this library, and no xUnit/runtime dependency or environment loader was introduced into the shared assembly. That structural reusability does not cure semantic misclassification. Rerun the focused contracts, new counterexamples and integration opt-out/discovery at the remediation commit before a new acceptance recommendation. Keep original failures and version identities attached to their exact reviewed source. No merge, BQ-1b start, live execution or publication is authorized by this review.
