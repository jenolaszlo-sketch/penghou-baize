# Tool argument integrity roadmap

Status: P0 items complete on main; the first authoritative adapter beyond
the structural default is still open.

Baize currently repairs malformed tool arguments through Nuwa, exposes whether
that structural repair was accepted, preserves rejected calls as
`InvalidArguments`, and returns typed CLR mapping failures without throwing.
The remaining work is deliberately separated from JSON repair: authoritative
schema policy and request-level tool declaration integrity belong to Baize.

## 1. Replaceable authoritative schema validation

Nuwa validates the structural JSON Schema subset needed to guide deterministic
repair, including types, required properties, nested items, enum/const values,
and `additionalProperties: false`. It intentionally is not a complete JSON
Schema implementation. Baize must therefore avoid presenting Nuwa acceptance
as proof that every keyword in an arbitrary schema dialect was enforced.

Introduce a provider-neutral validation boundary, `ILlmToolArgumentValidator`,
with a structural default and DI replaceability. Landed:
`ILlmToolArgumentValidator`, `LlmToolArgumentValidationResult` (validity,
path-aware failures, validator identity, explicit unsupported keywords),
and `StructuralToolArgumentValidator` run after Nuwa repair in both the
normalizer and the extractor, marking failures `InvalidArguments` with
validator diagnostics on the call. `AddLlmTools` registers the default;
adapters replace it with `TryAddSingleton`.

Acceptance criteria:

- validation runs after Nuwa repair and before a call is reported as fully
  normalized or passed to typed result mapping;
- validation returns typed, path-aware errors and never mutates arguments;
- the result distinguishes syntax/repair acceptance, schema validation, CLR
  mapping, and application-domain validation;
- dialect and unsupported-keyword behavior are explicit rather than silently
  ignored;
- applications can replace the validator through dependency injection;
- tests cover constraints outside Nuwa's repair subset, such as string length
  or pattern, numeric ranges, and array cardinality;
- diagnostics and telemetry record validator identity and outcome without
  retaining sensitive argument values;
- documentation accurately describes the default structural guarantee and any
  stronger adapter guarantee.

This belongs in Baize rather than Nuwa: Nuwa decides whether deterministic
repair produced an acceptable structural candidate; Baize owns tool contracts
and the policy for accepting a model tool call.

## 2. Duplicate tool declaration rejection

`ContentToolCallExtractor` and `LlmResponseNormalizer` currently group tools by
name and select the first declaration. Duplicate names make schema selection
order-dependent and can validate arguments against the wrong contract.

Add one shared request/tool-set validator and invoke it at the earliest common
Baize boundary. Provider adapters, normalization, and extraction must consume
the already-validated declaration set rather than each implementing a different
duplicate policy.

Acceptance criteria:

- blank and duplicate tool names are rejected before provider I/O or repair;
- comparison semantics are explicit and consistent with provider portability;
- the error identifies the duplicate name without including tool arguments;
- declaration order never selects which duplicate schema wins;
- native and pseudo-tool-call paths enforce the same rule;
- direct normalizer/extractor callers receive the same deterministic failure as
  ordinary client callers;
- regression tests cover exact duplicates, case variants, and different schemas
  under the same name.

## Sequence

Implement duplicate declaration rejection first because it is small and closes
an ambiguity before any validator abstraction consumes the tool set. Design the
replaceable validator second, using real Guyabano failures to choose the first
authoritative adapter and the required diagnostics.

Do not combine either item with tool execution, authorization, retries, or
application-domain validation. Those remain host/workflow responsibilities.
