# Decision record: evidence-driven model qualification

Status: Proposed, 2026-10-22. Approval is outstanding. Applies to the
[qualification roadmap](model-qualification-plan.md), not production routing.
The repository uses focused design/plan documents rather than a numbered ADR
directory; this is the equivalent decision record.

## Decisions and credible alternatives

| Question | Selected approach | Alternatives and tradeoff |
| --- | --- | --- |
| 1. Executable, tests or hybrid? | Non-packable net10.0 CLI plus shared scenario library consumed by current xUnit tests. Keep wrappers/traits. | A subprocess loop over `dotnet test` is initially small but environment/global capture, per-request budget interception and resumable structured assertions make it brittle. Direct xUnit method invocation couples runner to test runtime. A new independent suite duplicates assertions. Hybrid costs an incremental extraction but has one contract source. |
| 2. Inventory and dialects? | JSON records embed `LlmEndpointOptions` fields and effective `LlmEndpointCapabilities` snapshots; add provider-service identity, protocol/API version, revision and provenance. Dialects are explicit supported adapter settings. | Model-name inference incorrectly transfers DeepSeek native behavior to NVIDIA hosting. New provider enum conflicts with extensibility. Free-form wire patches could weaken validation; disallow them. |
| 3. Smallest durable evidence? | Frozen manifest plus sequenced append-only events and content-hashed immutable attachments, derived summary. Local single writer; no database. | TRX alone omits dispatch/economics/identity. Mutable latest-results JSON loses failures. SQLite is a credible later index for scale, not needed for tens of models. A hash chain detects accidental corruption but is not an independent signature. |
| 4. On-demand versus stored catalogue? | Both, with different authority: deterministic on-demand projections plus optional immutable reviewed snapshots pinned to input hashes/policy version. | Only a mutable catalogue obscures provenance. Storing every projection unnecessarily duplicates data. Approved snapshots never acquire authority merely by being generated. |
| 5. Endpoint quirks without name conditionals? | Structured exact endpoint/API/dialect/revision scope, declarative exclusions/narrow capability settings and reviewed adapter code for semantic transforms. | Registry-driven arbitrary transforms are unsafe. Broad global provider switches overgeneralize. Scope lists are versioned data; unknown scope does not inherit adaptations. |
| 6. Enough evidence for Verified? | One fully completed exact-contract pass establishes a dated observation. Default current qualification requires all three predeclared first trials to pass, with no unresolved contradictory eligible evidence; operational promotion has additional gates. | One smoke pass is too weak for the current profile. “Retry until green” erases reliability. Large benchmark samples are costly and unnecessary for protocol discovery. Three trials are a pragmatic gate, not statistical certainty. |
| 7. Capability versus reliability? | Capability claims attach to explicit assertion/facet/configuration. Advisory reliability records denominators, first-pass and retry outcomes, windows and censoring separately. | One universal score confuses rate limits, model behavior and parser correctness. Routing weights are host-owned future work. |
| 8. Stale/contradictory evidence? | Preserve original observation status; projection adds freshness, conflicts, blockers and approval eligibility. New revisions/configurations form new identities. Mixed conclusive pass/fail becomes Unstable; later recovery requires an explicit reviewed epoch. | Last-write-wins hides failures. Expiring evidence by deleting it destroys reproducibility. Stale does not mean unsupported. |
| 9. Reusable by Zhinu? | Frozen plan, case/shard IDs, CLI run/resume/project/compare, immutable bundle and exit/result protocol. | Implementing a workflow engine in Baize duplicates Zhinu. A distributed budget allocator is a future host concern; disjoint signed/approved envelopes suffice for bounded fan-out. |
| 10. Safe local resume? | Exclusive run lock, durable intent/reservation before dispatch, commit terminal results before progress, conservative unknown-spend accounting. Resume only unsent cases automatically. | Exactly-once remote inference is impossible without provider idempotency. Replaying ambiguous POSTs can duplicate spend. Whole-suite restarts waste requests and hide partial evidence. |
| 11. Prevent agent contamination? | Trusted runner evaluates immutable versioned assertions; append-only outcomes; separate untrusted annotations. Reviewed code/policy changes create new manifests. Agents have no evidence-write/promotion permission in unattended operation. | An LLM judge or autonomous threshold editor cannot establish independently checked compatibility. File hashes alone do not stop a privileged editor; host ACLs and separate storage/approval boundaries are required. |
| 12. Coexist with GM work? | Reuse published chat semantic and HTTP seams, keep chat-only scope initially, map evidence to GM gates without closing them. | Waiting for all GM milestones unnecessarily blocks local chat tooling. Claiming HTTP interception proves semantic batch/generation governance is incorrect. |

## Consequences and approval scope

No new public packages, public capability flags, provider SDKs or routing ports
are needed for BQ-1. Tool projects can use internal types and the already adopted
neutral transport packages. Shared contracts may reference existing Baize
validation code, but independent assertions must not simply compare a parser's
output with that same parser's output. Deterministic expected fixtures and
canonical schema checks provide the independent oracle.

The runner budget is a local execution allowance, not a replacement for a host's
accounting service. It enforces conservative caps for one approved plan, records
unknown consumption, and exports reservation/evidence references later. It cannot
know spending by unrelated processes on a shared provider account.

Approval of this decision record authorizes the architecture, not execution,
funding, a schedule, production profile promotion, adapter workarounds or release.
Those actions have separate concrete review gates in the roadmap. Until then,
all selections here remain proposed.
