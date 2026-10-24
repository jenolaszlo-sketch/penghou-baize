# BQ-1a remediation decisions

Date: 2026-10-23; R1/R2 correction updated 2026-10-24. Scope: qualification
contracts and their offline fixtures only.
Status: implemented decisions awaiting independent re-review; not BQ-1a acceptance.
This supplements the [original design](model-qualification-plan.md) and responds to
the [independent review](reviews/bq1a-focused-independent-review.md). It does not
change production capability flags, adapters, routing or historical model claims.

## Evidence and version boundary

The reviewed source is implementation `7b7b3b57365b1075b46cdffd65f0e48dd4350b02`,
with handoff baseline `dab7c8f7efe3274e7285fc97bbc4f1d407633857`. Commit
`b169d6ab66a211b611183d1d44e97f0419a20b06` preserves the original review,
executable probes and their 16 recorded outputs before remediation.

The subsequent [technical re-review](reviews/bq1a-remediation-independent-rereview.md)
and its 33 challenge outputs are preserved at
`2a5ab3efc9155fad30a88511acde7a4d6fbfa61a`, reviewing implementation
`68cd12a8ca1a019f7b863db87a1f62e6c10eebdd`. That implementation and this record's
prior contents remain reproducible at that exact commit. This working record
now corrects R1/R2 only; the reviews, handoffs and recorded probe results are
unchanged. The [focused handoff](model-qualification-bq1a-r1r2-handoff.md) binds
the corrected implementation and a separate replay of the same challenge source.

`QualificationContracts.cs`, `QualificationScenarios.cs`, their fixtures, the
original 47 tests and the probe sources/results remain unchanged. Their
`chat.baseline@2`, `tools.single@2` and `chat.streaming-integrity@1` meanings remain
executable historical behavior, including the recorded false acceptances. They
are not the recommended entry points for new qualification.

`QualificationContractsV3` / `QualificationScenariosV3` introduce
`chat.baseline@3`, `tools.single@3` and `chat.streaming-integrity@2`. The fixtures
are unchanged at `baize-token@1`, `echo-value@1` and `observed-stream@1` because
their request bytes, prompts, temperature 0 and 512-token cap did not change.
The two existing live-wrapper method names and traits are preserved, and the
wrappers explicitly move to the new contracts. This is a documented semantics
migration, not a reinterpretation of old evidence or a newly qualified model.

**R1/R2 version decision:** retain these unaccepted V3 identifiers. The available
repository records contain only deterministic fixtures and review evidence for
V3, explicitly awaiting acceptance; no accepted/published model qualification
claims were found. Locally known refs/tags place `68cd12a` only on this working
branch, with no containing remote-tracking branch or release tag. This is not a
complete audit of external consumers. Offline review artifacts already exist
and retain their exact build binding; they must never be reinterpreted as the
corrected results. A V3 identifier alone does not distinguish these builds.
If external V3 qualification usage is found, isolate that cohort and evaluate a
new contract version before publishing or consuming corrected qualification
evidence. No live qualification, evidence projection or claim migration occurs
in this correction.

## R1: nullable provider completion observations

Provider completion state starts unknown (`null`). A non-null `Done` observation
replaces the last explicit value: false means incomplete and true means complete.
A null field is missing information and never clears either value. Thus
false/null remains incomplete, true/null remains complete, false/true completes
normally, and true/false becomes incomplete until a later explicit true.
Metadata-only trailers do not invent a completion transition.

Unknown remains distinct from incomplete: the existing contracts allow missing
provider completion flags when other required finish/completion observations
hold, without asserting that the provider reported `Done=true`. The absence of
provider completion evidence does not by itself prove incomplete behavior.
Successful router diagnostics/activity telemetry cannot override a known final
false flag. Baseline, native-tool and routed regression fixtures enforce these
rules, including the independent counterexamples and positive controls.

## F2: response shape

Baseline requires no native tool calls. The single-tool fixture requires no
substantive ordinary answer text: empty/whitespace-only text is allowed as
formatting, including CR/LF/tab; reasoning remains an independent channel. Both
paths require bypassing the repair path. New named assertions expose these
conditions independently from value, schema, finish and reconstruction.

## F3: router observations

When router diagnostics are required, a successful scenario requires exactly one
terminal diagnostic snapshot with exactly one successful attempt and zero failed
attempts, following the finish event (or sharing it). This follows the current
public `LlmRouterDiagnostics` contract: the success diagnostic is its final event.
Zero attempts, a sole failed/unknown-outcome attempt, premature success, multiple
snapshots and multiple attempts cannot prove success. If diagnostics are optional
but supplied, they must still be coherent; opting out of observation cannot hide
contradictory evidence.

Results retain each numeric snapshot's attempt/success/failure counts and
terminal-order validity, without endpoint names or error messages. The summary
counts describe the last snapshot; the preserved list retains earlier failures
even when a later snapshot reports success. An incomplete execution keeps
its observed counts but does not claim dispatch success. Additional successful
snapshots are not an approved incremental-reporting protocol here. Future
multi-attempt/incremental semantics need a separate reviewed version.

This is an integrity observation, not physical request admission. Neither a
router record nor a StreamCompleted Activity event is a transport receipt.

## F4: cancellation source and incomplete observations

Caller cancellation and the explicit context deadline have separate tokens; the
execution token links them. If both fire, caller cancellation takes precedence.
A fired supplied deadline or a typed `TimeoutException` establishes timeout;
an unrelated/bare cancellation exception without either signal is
`ambiguous-cancellation` / Inconclusive. No timeout is inferred merely because
the caller did not cancel. Listeners and stream enumerators still dispose.

Generic provider invalid-request responses are Blocked; an untyped execution
exception is Inconclusive. Before a final response exists, content/value/schema/
reconstruction assertions that need it are NotObserved. Observed violations,
such as an undeclared tool, repair-path evidence or conflicting delta identity,
remain visible even when the overall execution is interrupted. Operational
failure assertions remain explicit. These outcomes do not assign remote model
fault, prove unsupported features or replace the future projector's attribution.

## D1: three separate identity claims

1. **Native tool validity:** one expected native function, unambiguous original
   JSON, full canonical schema, exact value, allowed response shape and no repair
   path. This does not depend on the provider choosing an OpenAI-style wire ID.
2. **Canonical correlation:** nonnegative tool index, consistent names/IDs/known
   part indices across fragments, exact name/arguments preservation into the
   collected call and a nonempty canonical call ID. Required stream IDs must
   match the collected ID ordinally. Index-based correlation is turn-local; it
   does not prove cross-turn continuation or deterministic IDs across runs.
3. **Provider identity provenance:** a separate typed assessment of applicability
   and observation, never inferred from the spelling/existence of a normalized
   ID. At this public event boundary provenance is always NotObserved. For a
   proven, revision-bound protocol without wire call IDs, applicability could
   be NotApplicable. For optional or required wire IDs it is Applicable but
   still NotObserved here. An unbound Ollama revision is Undetermined, not
   evidence that wire IDs are absent.

`QualificationResultV3.Succeeded` covers only its two named scenario/integrity
contracts. It does not turn the separate provenance assessment into a pass. A
consumer needing provider-emitted identity must require independent boundary
evidence; this change supplies no wire-capture or provenance-verification port.

### Explicit protocol applicability

The caller binds `ToolIdentityProtocol`; an unspecified or unknown profile blocks
the tool scenario before invocation. It is not guessed from model names or ID
patterns. Integration wrappers map their already selected built-in adapter to its
documented protocol. A custom adapter/alternate API needs its own reviewed binding.

| Explicit protocol | Stream-ID rule | Canonical ID origin when omitted | Provider identity applicability |
| --- | --- | --- | --- |
| OpenAI Chat Completions | Required nonempty emitted ID; missing ID fails | Collector fallback cannot satisfy required identity | Applicable, NotObserved |
| Anthropic Messages | Required nonempty emitted ID; missing ID fails | Collector fallback cannot satisfy required identity | Applicable, NotObserved |
| Ollama native `/api/chat`, revision unbound | Correlate using tool index; no normalized-ID requirement when absent | CollectorAssigned, explicitly labeled | Undetermined (`protocol-revision-unbound`), NotObserved |
| Gemini generateContent | Optional wire ID; require consistent normalized identity if present | CollectorAssigned if absent at the public seam; current adapter usually supplies an ID | Applicable, NotObserved |

Protocol rationale, checked 2026-10-23:

- [OpenAI's streaming reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/streaming-events)
  includes tool-call index and ID. The reviewed OpenAI adapter forwards IDs from
  tool deltas rather than manufacturing them. A fragment may omit an ID after it
  has been received; the completed call still requires one.
- [Anthropic's tool lifecycle](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls)
  binds tool results through a tool-use ID. This is a protocol rule, not a model
  family exception. Missing normalized IDs cannot qualify this composition.
- Ollama revision evidence is described below. A no-ID example or an adapter's
  `Id:null` output does not establish protocol-wide inapplicability.
- [Gemini's FunctionCall schema](https://ai.google.dev/api/generate-content#FunctionCall)
  marks its call ID optional. The reviewed adapter substitutes `call_N` when
  absent. The new evidence never represents that substitution as a provider ID.

The public stream cannot distinguish a supplied Gemini ID from its adapter's
fallback. It can validate native arguments and canonical correlation without
pretending to establish that provenance. Ollama's current collector-generated
GUID is also not provider provenance. No IDs are synthesized by these scenarios.
No globally permissive missing-ID exception or model-name conditional was added.

### R2: version-scoped Ollama evidence and boundary limits

The historical [Ollama v0.12.0 API types](https://raw.githubusercontent.com/ollama/ollama/v0.12.0/api/types.go)
define `ToolCall` with only `Function`. This supports a statement about that
tag's wire type; it does not bind any running server or all Ollama revisions.
The [main API types](https://raw.githubusercontent.com/ollama/ollama/main/api/types.go),
rechecked 2026-10-24, define an optional `ToolCall.ID` (`id,omitempty`) and a
message tool-call correlation field. Main is mutable and is not an immutable
release/revision binding for a qualification run. These sources differ; neither
authorizes an unversioned claim that Ollama universally lacks IDs.

The current qualification context has no established Ollama protocol revision.
It therefore reports applicability **Undetermined**, code
`protocol-revision-unbound`, and provider identity status **NotObserved**. This
is the same classification for ID-present and ID-absent offline wire fixtures.
There is no new version-binding port or guessed model/server-version exception.

The unchanged production `OllamaToolCall` wire model has no ID property, and
`OllamaChatClient` emits `Id:null` even if the input fixture supplies a wire ID.
The collector then assigns canonical correlation identity. Both native validity
and turn-local correlation can pass; neither proves the provider emitted the
canonical ID or that wire identity is inapplicable. Real-adapter tests exercise
both wire shapes without persisting their fake IDs in evidence. Forwarding IDs
and verifying their wire provenance remain separately authorized adapter/BQ-3
work. BQ-1b must bind the actual subject/configuration/protocol revision before
making a revision-scoped applicability claim. Required normalized IDs for
OpenAI and Anthropic and optional-ID Gemini behavior are unchanged.

## Additional review decisions

**Duplicate JSON members:** reject them recursively in objects, including those
inside arrays, using decoded member names and ordinal equality. Both member
orders, identical repeated values and escaped duplicate names fail a separate
`tool.json-unique` assertion. Never choose a duplicate's last value to satisfy the
fixture. Continue using `StructuralToolArgumentValidator` for schema semantics;
this is an unambiguous-evidence rule, not another JSON Schema implementation.
An invalid syntax observation cannot also assert that duplicate keys were seen;
uniqueness/schema/value checks are NotObserved until unambiguous JSON is available.
It observes arguments at the public stream boundary, not bytes an adapter might
already have changed.

**Repair provenance:** native qualification requires bypassing the repair path,
not merely ending with acceptable JSON. A repair flag, any nonempty attempt list
(including skipped attempts) or any detailed repair diagnostic prevents a native
pass, with `repair-path-observed`. This is deliberately a composition constraint:
it does not assert that the model's original bytes were invalid or that every
diagnostic indicates changed text. Validation-only/unchanged repair pipelines
remain a separate profile; contradictory diagnostics cannot manufacture native
provenance. Empty attempt lists without other repair metadata are allowed. No
repair operation is invoked by the contracts.

**Opaque IDs:** require nonempty strings where a stream ID is required or an ID
is supplied. Do not trim, normalize, impose a 256-character ceiling, reject
interior whitespace/control characters or infer provider provenance from ID
format. Even whitespace-only nonempty IDs are compared exactly; they are opaque
correlation tokens, not user-facing labels. Empty supplied IDs still fail.
Results never retain the ID itself. Resource limits belong at the execution
boundary rather than masquerading as remote ID syntax rules.

**Part association:** new streaming integrity rejects a known tool part index
changing across fragments. It still compares flattened content/reasoning and
tool arguments, not every ordered part or continuation/signature. The narrower
check does not claim full multipart fidelity.

## F1 and future-work ownership

F1 remains a fixture-confirmed **OpenAI adapter defect**, open for a separately
authorized adapter change. Whole-call and final-argument co-terminal fixtures,
direct and routed, must continue failing the strict sequence assertion until that
adapter work is reviewed. A payload and finish in one public event is permitted;
payload emitted after a separate public finish is not. No model variability or
unsupported-tool conclusion follows from this known adapter signature.

| Remaining limitation | Owner / gate |
| --- | --- |
| Physical HTTP dispatch admission, hidden retries, byte limits, deadline/idle enforcement, bounded cleanup and usage liabilities | BQ-1b execution and independent dispatch-path review; no runner implemented here |
| Wire/provider ID provenance and exact subject/configuration binding | BQ-1b evidence binding and BQ-3 protocol/adapter evidence; currently NotObserved, never inferred |
| Complete ordered-part, signature and continuation fidelity; multi-turn and parallel tool-result binding | BQ-4 shared sequential/parallel/continuation contracts, supported by deterministic core/provider regressions |
| Immutable evidence projection, corrections and cross-version cohorts | BQ-2; no projection or migration of old claims here |
| Repair-aware capability profiles and their original-versus-repaired outcomes | Separate structured/repair contracts in BQ-4; this native profile rejects repair-path use |
| OpenAI co-terminal emission ordering | Separate production adapter remediation; BQ-3 quirk record can reference F1 and these fixtures |

Green deterministic tests establish behavior under these fixtures, not BQ-1a
acceptance, live capability verification, merge permission or authorization to
start BQ-1b. The implementation/evidence handoff records the final commit and
independent re-review requirement separately.
