# Provider quirk registry design

Status: Proposed registry format, 2026-10-22. Seed observations below are drawn
from existing repository evidence; they are not new live results. Preserve the
[compatibility matrix](live-provider-compatibility.md) and
[verification log](live-provider-verification-log.md) as original sources.

## Record format and placement

Use one reviewed JSON file per stable ID in future
`docs/qualification/quirks/BQK-*.json`. Generate a concise Markdown index from
those records. Runtime observations only propose new records; no dynamic code
loading or response transformations are performed from this directory.

Required fields: `kind=provider-quirk`, `schemaVersion`, `id`, `recordVersion`,
title, provider-service and adapter keys, endpoint profile/origin/base path, API
version/protocol/dialect, observed model IDs/revisions, known applicability scope,
request predicates, observed behavior, expected canonical contract, discovery
date, last verification date, evidence references, minimal reproduction
(descriptor/version, fixture, configuration and expected/observed checks), root
cause/classification/basis, workaround status/description/code reference,
regression references, owner, review state, resolution and supersedes references.

Classification is `adapter-defect`, `provider-protocol-variation`,
`model-generation-variability` or `unresolved`. Lifecycle is `observed`,
`confirmed`, `mitigated`, `resolved`, `obsolete`; verification is independently
`legacy-only`, `fixture-reproduced`, `live-reverified`, `stale` or `blocked`.
Mitigated means an adaptation exists, not that the provider defect disappeared.
Resolved requires scoped evidence; the record remains after removal of a workaround.

Match exact endpoint/dialect/API first, then explicit model/revision set or a
documented family range, then request predicates. Never infer provider dialect
from a model name. New aliases/revisions do not inherit a family workaround
without reviewed scope. Multiple conflicting matches fail configuration rather
than use last-match-wins. Record both the narrower *observed* scope and any wider
current *code* scope so the latter does not become an unsupported live claim.

Configuration can narrow capabilities, exclude a contract, or choose a documented
mode/timeout/budget. Code owns wire translation, stream parsing and lifecycle
semantics. An unrepresentable provider variation stays blocked/unsupported for
that contract until a separately reviewed adapter change. Registry entries may
reference existing code but may not inject expressions, arbitrary JSON patches,
model-name switches or executable hooks.

## Seed records for BQ-3

All dates here refer to the **2026-08-12** existing live history. Exact historical
runner/package hashes are not established by the narrative and must remain
unknown in imports. Last live verification is that date, not this design date.
No new evidence is fabricated to complete a record.

### BQK-0001 — Gemini canonical schema keyword rejection

- Scope observed: native Gemini `v1beta` generateContent/streamGenerateContent,
  models identified in the compatibility matrix (including 3.6 Flash and 3.5
  Flash-Lite). Broader current code scope: Gemini schema adapter. Not NVIDIA
  hosting or an OpenAI-compatible Gemini gateway without separate evidence.
- Observed: provider rejected `additionalProperties`; expected: Baize canonical
  JSON Schema remains authoritative for local acceptance, including rejecting
  extra properties. Root cause: provider-protocol-variation, legacy live narrative
  plus deterministic adapter tests.
- Minimal reproduction: single-tool or structured fixture with object properties,
  required field and `additionalProperties:false`; compare wire rejection before
  adaptation with adapted request and canonical validation of an extra field.
- Existing mitigation: `src/Penghou.Baize.Gemini/GeminiSchemaAdapter.cs` transforms
  the wire copy and records lossy adaptation. Canonical input is preserved.
- Regression: `GeminiSchemaAdapterTests.Adapt_RemovesUnsupportedKeywordRecursively`,
  `Adapt_PreservesPropertyNamedAdditionalProperties`, and
  `GeminiChatClientTests.StreamAsync_AdaptsToolSchemaForGeminiWireDialect`.
  BQ-3 must link existing strict-validation negative coverage or add a focused
  missing negative case; wire removal alone does not prove local enforcement.
- Status: mitigated; fixture-reproduced/legacy-only live provenance, stale for
  operational promotion. Removal requires exact endpoint requalification and
  deterministic canonical-validation tests, not a documentation change alone.

### BQK-0002 — DeepSeek native JSON Schema unavailable

- Scope observed: `https://api.deepseek.com/v1`, OpenAi adapter `DeepSeek` dialect,
  `deepseek-v4-flash` and `deepseek-v4-pro`; native schema response mode. Separately
  recorded Claude-compatible tool-backed behavior is a different API subject.
- Observed: `response_format.type=json_schema` returned HTTP 400 unavailable;
  expected: explicit native-schema support or truthful alternative classification.
  Root cause: provider-protocol-variation. `json_object` validity is not schema enforcement.
- Minimal reproduction: the existing value/count schema sent through native-schema
  mode; preserve rejection and then run a separate tool-backed profile.
- Existing mitigation: `OpenAiDialectPolicy`, `OpenAiStructuredOutput` and
  `OpenAiChatCompletionRequestMapper` select a forced synthetic tool. Record
  `StructuredOutputViaTool` separately from `NativeStructuredOutput`; do not credit
  a native pass. Current factory may configure native capability true for OpenAi;
  qualification must record the actual effective mode and adaptation.
- Regression: `OpenAiChatClientTests.StreamAsync_DeepSeekSchemaUsesForcedSyntheticToolAndReturnsContent`;
  add/link canonical-invalid-result and Standard-dialect unaffected cases during
  BQ-3. Never apply this to NVIDIA's DeepSeek model solely from its publisher name.
- Status: mitigated, legacy live evidence; native facet Unsupported only within
  recorded historical conditions. Current live support remains unverified.

### BQK-0003 — DeepSeek forced tool selection with thinking

- Same native OpenAI-compatible endpoint/models/dialect as BQK-0002; predicate is
  synthetic schema tool selection plus provider-default thinking.
- Observed: forced synthetic `tool_choice` was rejected with thinking active.
  Expected: preserve explicitly requested thinking and canonical schema guarantees.
  Root cause: provider-protocol-variation, not a general reasoning failure.
- Minimal reproduction: BQK-0002 fallback request with default thinking, then the
  same request with explicitly enabled thinking and an ordinary thinking-only request.
- Existing mitigation: mapper emits disabled thinking only for synthetic fallback;
  incompatible explicit thinking is rejected instead of silently overridden.
- Regression: `StreamAsync_DeepSeekSchemaUsesForcedSyntheticToolAndReturnsContent`,
  `StreamAsync_RejectsExplicitThinkingWithDeepSeekSchemaFallback`, and
  `StreamAsync_MapsThinkingModeAndToggle` in `OpenAiChatClientTests`.
- Status: mitigated with deterministic coverage and legacy live evidence. Future
  native provider change requires both live recheck and unaffected-mode tests.

### BQK-0004 — Gemini thinking controls and shared output budget

- Scope: native `v1beta`; `gemini-3.6-flash` rejected `thinkingBudget:0`;
  separate recorded tool-call truncation with 128 output tokens, 118 thinking
  tokens, and successful completion at 512. Do not broaden the disabled-control
  rejection to every Gemini model.
- Expected: settings unsupported by a model are rejected truthfully; output
  exhaustion is not missing tool capability. Classification: provider-protocol-
  variation for control acceptance, model/budget interaction for truncation.
- Minimal reproduction: disabled-thinking request; separately run the single-tool
  fixture at the two explicit output caps, comparing terminal/usage/call checks.
- Mitigation belongs in capability/profile and request configuration:
  `ThinkingDisable=false` for the scoped endpoint; a predeclared adequate output
  cap. Never dynamically raise spend or remove an explicit control to earn a pass.
- Regression: `GeminiChatClientTests.StreamAsync_ThrowsForDisabledThinkingWhenUnsupported`,
  `StreamAsync_EmitsZeroBudgetForExplicitlyDisabledThinking` and
  `StreamAsync_ExplicitThinkingBudgetWinsOverEffortMapping`. Future runner tests
  must distinguish MAX_TOKENS from unsupported tools using a deterministic fixture.
- Status: profile mitigation documented; last live verification legacy/stale.

### BQK-0005 — Model listing does not establish API availability

- Scope: Gemini 2.5 Flash-Lite, Flash and Pro, listed by Models API but rejected
  for new users through the tested native `generateContent` style. Paid access
  did not change the recorded outcome; the narrative identifies Interactions
  availability as a separate API route.
- Expected: exact model/API/account availability established by invocation,
  independently of discovery. Observed 404 is access/API-style specific.
  Classification: provider-protocol-variation/access restriction; not a chat parser bug.
- Minimal reproduction: preserve discovered ID/source, then baseline on exact
  `v1beta` endpoint and scope with provider 404 code. No speculative automatic
  switch to Interactions or a different model.
- Mitigation: dated exclusion/Blocked scope in inventory; no adapter transform.
  Existing evidence is the matrix/log; no dedicated discovery-runner regression
  exists. BQ-1/3 adds fake listing-success/invoke-404 tests with account/API identity
  and zero false Verified/Unsupported promotion.
- Status: observed/legacy-only; live recheck pending authorization. Other providers'
  listing discrepancies are new records, not expansion of this one without evidence.

### BQK-0006 — Gemini malformed calls on preview model

- Scope: `gemini-3.1-pro-preview`, native API, simple and parallel tool fixtures.
  Observed `MALFORMED_FUNCTION_CALL` with empty call twice while sequential tools
  passed. Expected: complete valid call with bound arguments and continuation IDs.
- Classification: model-generation-variability in the existing narrative; deeper
  root cause unproven. Minimal reproduction is the unchanged single/parallel
  descriptor with original sampling/output settings and both attempts retained.
- Workaround: none that may manufacture a successful call. Mark affected facets
  Unstable; exclude from an approved operational tool profile if warranted. No
  arbitrary tool synthesis or argument repair claimed as native success.
- Regression: provider-error/empty-call fixture to be linked or added in BQ-3;
  bounded repetition tests in BQ-4 must retain failures and separate sequential
  success. Status observed, legacy-only, unresolved provider root cause.

### BQK-0007 — Gemini batch response/download shape drift

- Scope: native Gemini v1beta batch models listed in existing history, paid tier.
  Observed current `response.responsesFile` and `/download/v1beta/files/{id}:download`
  differed from old adapter fixture assumptions. Expected: correct result
  correlation and retrieval without widening URL/credential authority.
- Classification: adapter compatibility gap/provider protocol evolution, as
  documented; avoid claiming a provider bug. Reproduction: replay both old/new
  status envelopes and expected result URL using synthetic batch fixtures.
- Existing mitigation and regression anchor: `GeminiBatchClient` and
  `GeminiBatchClientTests` status/results tests. BQ-3 resolves exact test IDs and
  evidence references before formal record confirmation.
- Status: mitigation recorded historically; excluded from BQ-1 chat scope.
  This does not close GM-3 semantic handle/operation authorization.

## Workaround acceptance and retirement

Each new workaround needs a smallest reproducible fixture, scope-match and
scope-nonmatch tests, original versus adapted request metadata, and canonical
validation/stream/error/security invariants. For provider variation, keep both
wire contracts inspectable. For an adapter defect, reproduce the bug without a
live API and prove the corrected normalized output. For stochastic behavior,
preserve the failure; do not add parser tolerance merely to improve pass rate.

Reject adaptations that drop tool results, invent missing streamed text, suppress
terminal errors, silently change explicit reasoning, weaken schema acceptance,
follow unapproved URLs or change credential scope. Configuration must not be able
to enable such behavior. Proposed fixes are ordinary reviewed production changes
outside this design assignment, with explicit contract versions where semantics
change. Retirement requires current scoped evidence, unaffected-scope regression
passes and a retained historical record explaining the removal.
