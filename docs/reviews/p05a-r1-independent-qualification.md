# Independent qualification disposition

**Disposition: B — ACCEPTED WITH QUALIFICATIONS. Publication of the source and sanitized qualification summary is permitted for hosted CI.**

This disposition is bound to commit `39577e4f57aa17f282efc02feb28dd4b1d73028a`, tree `9994f4f3e57f2e449eba16021c29f3553ec73349`, parent `64b799bc30699f7f19b07fdb2d49ff26b72ef7aa`.

## Independent checks

- Fresh Windows checkout: ordinary direct qualification restore/build/test passed 337/337; Release solution build and format verification passed.
- Independent full Windows solution rerun: 26 project/TFM runs, 2,150 passed, 0 failed, 28 skips. The same 14 unique capability-gated Gemini/OpenAI tests were skipped as in the frozen P05A run. The capability differences are provider queuing, operation retrieval/cancellation, and candidate-count support.
- Fresh native ext4 checkout under Ubuntu in WSL: direct qualification passed 337/337; Release build, full suite (2,150 passed, 0 failed, 28 skips), and format verification passed. This is a Linux environment check under WSL, not a physical Linux host run.
- Package verification produced 12 package archives and 12 symbol archives with no inventory issues. Dependency closure passed for the 12 source projects, and the qualification probe/test files were absent from packages.
- The candidate changes only build/qualification configuration and qualification test code. Production `src/` and frozen qualification evidence are unchanged from the original integrated R1 candidate.
- Frozen binding checks remain intact: the separate review ref is `e4f8e132c10f199a1754ae629ea95ceac19c0255`; the original R1 plan/proposal bytes match their recorded digests; the 81-file historical deployment closure, runner, and canonical manifest digest were independently verified. The historical plan remains bound to its original runner; this remediation build is not asserted byte-identical to that runner.

## Qualifications and remaining limits

The first independent full Windows run on this candidate had one failure in `Stage4aLoopbackTests.TimeoutAwaitingHeaders_NoRetry` (expected one request, observed zero). The test passed when isolated, and the complete Windows suite passed on the subsequent independent rerun. Treat this as a reproducibility caveat; if hosted CI reproduces it, investigate before claiming final acceptance.

Hosted CI has not run, and macOS has not been exercised locally. Publication is permitted to obtain the hosted platform matrix; final qualification acceptance requires the hosted Ubuntu and macOS workflows to pass. The local Linux result was under WSL.

The 28 skips are the preserved, capability-gated provider conformance cases; no live provider calls or credentials were used. This report permits source and sanitized evidence publication for hosted validation. It does not authorize live provider execution or package release. Existing operational limits remain: SQLite rollback/cloning, trusted host clock, a remaining hash-to-use race, no network or multi-host registry qualification, and unknown provider-side routing, retries, billing, and execution counts. The proposal still records authorization, execution authorization, and qualification as false.
