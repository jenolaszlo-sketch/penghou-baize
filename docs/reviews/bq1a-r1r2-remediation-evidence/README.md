# R1/R2 corrected challenge replay

Corrected implementation: `123dd86249eba57be700d5e32b486a7527e06474`.
Captured 2026-10-24 (Asia/Manila). See the
[focused handoff](../../model-qualification-bq1a-r1r2-handoff.md).

This directory records a new execution of the **unchanged** 33-case
[re-review probe source](../bq1a-remediation-rereview-probes/Program.cs).
Its original outputs and the original review remain untouched at preservation
commit `2a5ab3efc9155fad30a88511acde7a4d6fbfa61a`. The old implementation under
review remains `68cd12a8ca1a019f7b863db87a1f62e6c10eebdd`.

`binding.json` identifies the source blob and corrected implementation. Its
SHA-256 covers result text encoded as UTF-8 without BOM, with CRLF normalized to
LF (including the final newline), so Git checkout line endings do not change
the binding. `resultsGitBlob` records the repository-normalized blob as well.

Run `Verify-ChallengeReplay.ps1` with PowerShell 7. It checks that hash and compares
the 33 records with the old outputs, modifying expected properties **in memory**:

- Three R1 records change from false success to Failed dispositions and the two
  completion assertions fail. Their other fields remain identical.
- Both Ollama records change only identity applicability/code to
  Undetermined / `protocol-revision-unbound`; native validity/correlation remain
  Passed and provider provenance remains NotObserved.
- The other 28 records remain identical, including OpenAI F1 and its split
  control, F2/F3/F4 witnesses and completion positive controls.

The probe executable reports observations; exit 0 only means all cases ran.
The comparison is the explicit R1/R2 expectation check. In-memory handlers and
stream fixtures perform no live/paid provider calls. Build the corrected Release
qualification assemblies and then the original probe project before replaying.
Use a new output path for another run; never overwrite either preserved cohort.

These are deterministic correction evidence, not accepted model qualification
claims or an independent acceptance sign-off.
