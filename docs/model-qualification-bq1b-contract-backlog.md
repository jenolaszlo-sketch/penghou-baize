# BQ-1b qualification contract backlog

Date: 2026-10-25. Repository: `jenolaszlo-sketch/penghou-baize`.

The accepted contracts (`chat.baseline@3`, `tools.single@3`,
`chat.streaming-integrity@2`) stay unchanged and authoritative. This document
records the contract gaps that matter for Baize graduation and a small,
prioritized backlog for later qualification contracts. It is a planning
record, not an implementation mandate: no contract here is designed, approved
or wired into the runner.

## Gaps against the accepted contracts

| Priority | Gap | Why it matters for graduation | Proposed direction |
| --- | --- | --- | --- |
| P1 | JSON Schema compatibility | Gemini restricts schema composition (no `$defs`/`$ref`, no `additionalProperties: false` in the strictest mode); OpenAI-family providers differ on strict modes. Baize's schema/structured-output path must be proven against each restriction before any "structured output works" claim. | A `schema.compat@1` family: emit a fixed reviewed schema set through the real adapter, assert accepted/rejected/repair outcomes per provider, and record the effective schema mode used. Never auto-rewrite the application schema. |
| P1 | Structured output behavior | Native structured output, synthetic-tool emulation and repair paths behave differently per provider; a passing baseline says nothing about them. | `structured.output@1` scenarios with strict pass/fail assertions plus repair-path observation, reusing the existing repair diagnostics rather than inventing new capture. |
| P2 | Reasoning/thinking settings | Model-specific reasoning configuration (budgets, efforts, disabling) changes request shapes and billing; an unknown mapping silently voids token/cost bounds. | `reasoning.config@1` scenarios asserting the emitted wire configuration for each supported setting combination, plus usage-bounding checks where the provider reports reasoning tokens. |
| P2 | Provider-specific configuration combinations | Dialect, thinking, schema-mode and tool combinations interact; global workarounds must never leak across models. | Per-provider combination matrices as reviewed fixtures, executed only for the exact subject binding. |
| P2 | Tool-call argument streaming variants | Providers fragment tool arguments differently (single JSON blob versus incremental fragments); reconstruction differences are a Baize-defect risk. | `tools.fragments@1` fixture variants with expected reconstruction outcomes per documented provider behavior. |
| P3 | Usage and cost semantics | Providers differ on prompt/thinking/total overlap, caching and derived totals; current accounting conservatively keeps unknown liability. | A provider-bound usage-completeness contract that narrows what counts as trustworthy complete usage, without weakening the conservative default. |
| P3 | Error-shape coverage | Error bodies, retry guidance (`Retry-After`) and status codes differ; current attribution treats unknown 5xx as provider-unavailable. | Scoped error-shape scenarios feeding the failure taxonomy, not new retry machinery. |
| P3 | Finish-reason vocabulary | Providers emit finish reasons outside the stop/tool-call set; current classification must not mislabel them. | A finish-reason fixture matrix per protocol revision. |

Rules applied when any of these are eventually built:

- Each new contract gets a versioned descriptor, its own fixture digests and
  explicit dispatch budgets; historical contracts are never reinterpreted.
- Contracts observe the same one-dispatch-per-scenario economy as today;
  observations (like streaming-integrity) share the parent dispatch.
- No contract weakens an accepted assertion, waives F1 or converts a missing
  observation into a pass.

## Quirk handling order (already applied in Stage 3)

When a real quirk is discovered the runner records it with the failure stage
and suggested attribution class, then prefers: (1) explicit configuration via
existing Baize capabilities, (2) a narrowly scoped adapter correction under
separate authorization, (3) a documented unsupported combination. The runner
itself never applies global provider workarounds.
