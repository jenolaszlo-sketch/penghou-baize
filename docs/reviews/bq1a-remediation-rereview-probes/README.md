# BQ-1a remediation re-review probes

Source under review: `68cd12a8ca1a019f7b863db87a1f62e6c10eebdd`.
The checked-out handoff `2898917f4dfefc476c184d281cb68c0c59513e53` changes
only the handoff document. See the
[review and commands](../bq1a-remediation-independent-rereview.md).

This standalone observation recorder references already built Release/net10.0
reviewed assemblies and links the unchanged `ScenarioFixture.cs`. Its assembly
name uses the existing friend-assembly identity to access internal contracts;
it is not a production/test-project modification. It has no package references;
`NuGet.Config` clears package sources. A .NET 10 SDK/framework pack and the
reviewed build's dependencies must be available locally.

All provider clients receive custom in-memory HTTP handlers. The handler returns
fixture text without opening sockets, resolving hostnames or using credentials.
The fake `.invalid` URLs are identifiers only. Other scenarios use the unchanged
in-memory stream fixture. There are no live model observations.

`results.jsonl` preserves 33 actual outputs from 2026-10-23. Each record contains
the case name, expected property, actual outcomes/assertions, safe identity and
router metadata and invocation count. The program deliberately has no final
acceptance assertion: exit 0 means replay completed. In particular:

- `R1-incomplete-then-unknown`, its tool variant and its routed variant all
  incorrectly pass every assertion. The explicit-false control rejects; normal
  intermediate-false-to-true and true-to-metadata controls pass.
- `R2-real-ollama-wire-id-present` passes native/canonical checks but incorrectly
  reports provider identity as protocol-inapplicable. Its fake wire input includes
  an optional ID that the actual adapter drops.
- `F1-real-openai-coterminal` fails strict sequence; the split-finish control
  passes. This is the independently reproduced existing adapter defect.
- Other cases check F2/F3/F4 closure, incomplete observations, JSON uniqueness,
  repair-path enforcement, opaque IDs and required/optional ID behavior.

Do not overwrite the preserved results when reviewing a later correction.
Capture a later replay to a different file and retain its exact build binding.
These probes are review artifacts, not new current qualification claims.
