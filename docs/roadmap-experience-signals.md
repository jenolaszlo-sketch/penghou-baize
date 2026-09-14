# Experience-informed routing signals roadmap

## Status

Planned. This roadmap aligns Baize with the Penghou memory and experience
architecture without making Baize an execution-history or learning system.

## Boundary

Baize owns provider/model execution, normalized capabilities, request mapping,
and routing mechanics. Hongxian owns historical execution evidence and derived
experience. Marang may compose an agent-facing decision view. The host owns
authorization, budgets, exploration policy, and the final routing decision.

Historical signals are advisory inputs. They never override current provider
capability, availability, policy, credential, content, or tool authorization.

## Phase 1 — Stable execution identity and metrics

- [ ] Define a bounded provider-neutral invocation identity/profile containing
  provider, endpoint, model family/version, role/profile, reasoning mode,
  capability snapshot, tool-schema identity, attempt/retry identity, and opaque
  activity/context/artifact references.
- [ ] Expose actual normalized token/usage, cost inputs where known, latency,
  rate-limit state, finish/failure classification, repair stages, tool-call
  validity, cancellation, and provider-operation handles without retaining full
  prompts or credentials.
- [ ] Preserve enough identity to distinguish the same model used under
  different prompts, roles, tools, reasoning modes, and provider versions.
- [ ] Make unavailable or provider-estimated economics explicit; do not invent
  comparable cost or token values.

## Phase 2 — External advisory routing evidence

- [ ] Add a small host-supplied routing-evidence port carrying contextual
  aggregates by task class/profile plus sample size, observation window,
  recency, provenance/checkpoint, and policy version.
- [ ] Keep score semantics opaque or explicitly typed. Do not normalize
  heterogeneous evidence into a universal reputation number inside Baize.
- [ ] Return a routing explanation identifying hard filters, current economics,
  advisory evidence considered, exclusions, fallback, and the selected route.
- [ ] Treat stale, degraded, truncated, contradictory, or unavailable evidence
  explicitly and preserve deterministic capability-only routing as a supported
  fallback.

## Phase 3 — Bias and feedback-loop evaluation

- [ ] Measure first-pass success, retry-adjusted cost, correction rate,
  validation outcomes, tool reliability, latency, and recovery outcomes by
  model/version/role/profile/task class.
- [ ] Require minimum sample and recency policies before advisory evidence can
  materially affect routing.
- [ ] Keep controlled exploration opt-in and host-owned; record its rationale so
  a model is not permanently starved of new evidence.
- [ ] Test selection bias, model-version changes, prompt/profile drift, missing
  data, and historical failure recovery before enabling automatic weighting.

## Non-goals

- Storing Hongxian history or Cangjie memory in Baize.
- A single global model ranking or self-updating hidden weights.
- Automatic promotion of model outputs into knowledge.
- Allowing historical success to bypass capability, safety, budget, or tool
  authorization checks.
- Making every provider expose identical cost, usage, or durability semantics.

## Gate

One consumer can route with and without the same immutable advisory evidence,
inspect why the decision changed, append the resulting invocation evidence to
Hongxian, and reproduce the routing inputs without Baize depending on Hongxian,
Marang, Cangjie, or Fuwen packages.
