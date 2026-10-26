# BQ1B Stage 4B R1 handoff

**Disposition: READY FOR INDEPENDENT R1 QUALIFICATION.** This means the bounded remediation and evidence are ready for independent review. It is not an independent acceptance and does not authorize a live provider request.

## A. Git binding

- Repository: `Penghou.Baize`.
- Implementation worktree/branch: `implementation/baize-bq1b-stage4b-r1`.
- Source baseline: `6e99d976a4e521c62b049983281606d04a03a96e`.
- Independent review commit: `e4f8e132c10f199a1754ae629ea95ceac19c0255`.
- Implementation commit: `85c1f63f6c785fefe10e311df67a6370ee7e462e`, directly parented to the baseline.
- The final evidence commit is the `HEAD` of this branch; its exact commit, tree, parent, and clean status are reported with the delivered Git binding. The baseline and original review evidence were not modified.
- No merge, rebase, reset, push, publish, or tag was performed.

The final evidence directory includes `changed-files.txt`, an inventory relative to the source baseline. It includes the new handoff and R1 evidence while leaving historical Stage 4B evidence intact.

## B. Implementation summary

- **F-01:** Added signed schema 2.0 authorization identity, explicit durable SQLite consumption, at-most-once claim, fail-closed storage handling, and no-live-resume dispatch. Design and scenario evidence are in [authorization-consumption-design](reviews/bq1b-stage4b-r1-evidence/authorization-consumption-design.md) and [the negative-test matrix](reviews/bq1b-stage4b-r1-evidence/authorization-negative-test-matrix.json).
- **F-02:** Bound the Release build and plan to a sorted, normalized 81-file deployment closure with file sizes and SHA-256 digests. The closure is verified before claim and again at dispatch admission.
- **F-03:** Reports distinguish authorization claims, Baize dispatch admissions, unique client transport attempts, observed HTTP responses, configured model, provider-reported route, and unknown provider-side execution count.
- **F-04:** Added a separately versioned `canonical-evidence-json@1` digest writer, duplicate-key rejection, and cross-platform vectors without changing existing `canonical-json@1` plan/authorization identities.
- **F-05:** `validate-live --evidence-out` writes canonical UTF-8 JSON without a BOM; a byte-level regression test covers this.
- **F-06:** Signed UTC issue/expiry timestamps are limited to ten minutes, checked before claim and again before dispatch; resume does not renew them.
- **F-07:** Credential readiness is checked before registry claim and again before dispatch. Values are never put in evidence or diagnostics.
- **F-08:** Renamed and corrected the keep-alive probe interpretation; it covers reconnect after separate admissions, not single-attempt replay prevention.

No transport profile, retry behavior, model support, or Stage 5 functionality was broadened.

## C. Authorization-consumption design

The one-time claim is a transactionally inserted row in an explicitly provisioned SQLite registry. The signed authorization binds a random authorization GUID, registry authority, proposal and plan hashes, selected work, endpoint/profile, limits, credential reference, and short validity window. A primary key on authorization ID and unique constraints on authorization reference and execution correlation ID prevent a second claim. SQLite is configured for WAL, FULL synchronous writes, foreign keys, and a checked busy timeout. The claim commits before dispatch; uncertain commit results fail closed. Failed checks, process exit, cancellation, provider error, or terminal reporting never remove the claim. See the design document for provisioning, permissions, failure cases, and operational limits.

## D. Replay and crash testing

Baseline defect reproduced offline: `Stage4bReplayBaselineReproduction.Baseline_StatelessAuthorizationValidationAllowsTwoOfflineDispatchAdmissions` passed against the unmodified baseline and admitted the same authorization twice into a simulated offline dispatch boundary. It made no provider request. Baseline results were Release build 0 warnings/errors, Stage 4B 16/16, and qualification suite 310/310; see `baseline-replay-reproduction.json` and `baseline-regression-results.json`.

Candidate results: 337 passed, 0 failed, 0 skipped. The matrix covers duplicate and re-encoded authorizations, in-process and subprocess races, process restart, crash after claim before dispatch, ambiguous commit acknowledgement, provider failure/timeout/cancellation, missing/read-only/corrupt stores, wrong authority, expiry, scope mismatch, missing credentials, and live resume. Cross-process claims use a file barrier and yield one receipt. No provider request or real network access occurred.

## E. Artifact identity

- Build revision: `85c1f63f6c785fefe10e311df67a6370ee7e462e`.
- Product version: `0.3.0-preview.7+85c1f63f6c785fefe10e311df67a6370ee7e462e`.
- Configuration/runtime: Release, `.NET 10.0.12`, `win-x64`.
- Runner: `Penghou.Baize.Qualification.dll`, 570,368 bytes, SHA-256 `sha256:8ef546b903bac17091dec5765ea58f6efb7afcdf9fa7d3d081de993374206697`.
- Deployment closure: 81 files; digest `sha256:5568df5da41137df42ed7391d0562ec93ef28de519dde1ec821b1efe3527990e`.
- Plan hash: `sha256:971ea862f91ccb6166e2b36402173d0dce262aa992ad72c9a204e548d43a6451`.
- Proposal digest: `sha256:5f0a7638799e4aaf8e1be4236fdf5e01207bec0d10e19c5fab1cea889a50f41`.

The new plan is live-blocked pending economics/protocol/token-bound evidence, model-revision identification, and explicit operator authorization. The proposal is review-only. It has not been signed as an authorization; no credential or authorization was issued. Historical accepted build `214a363`, runner SHA-256 `b7cb08f04262d37be05545e09d1b765ec931a3a416c5f6ddf6c71b81df4b36c2`, and closure digest `6d6b36de94dee51959124f283ae57906d29bd5d66439a261a8190dc1c9ac61fc` remain historical evidence only.

## F. Transport and workload compatibility

The profile remains `baize-sockets-http11-exact-v1`, physical-attempt bound 1, with redirects, proxy, cookies, decompression, authentication negotiation, retries, and hidden client replay disabled. The combined `chat.baseline@3` and `chat.streaming-integrity@2` workload records one synthetic invocation; streaming integrity adds no request. Baize's one-attempt bound does not guarantee exactly-once provider inference. Provider routing, retries, billing, model route, and provider-side execution count may remain unknown.

## G. Financial and credential invariants

The policy ceiling remains USD 0.0004, one model call, one HTTP request, 256 input tokens, 512 output tokens, zero reasoning tokens, and 768 total tokens. No provider credential was read; no provider request was made. Readiness is a nonempty-source check, not credential validation or account-allowance verification. See `credential-isolation.json`.

## H. Test results

- Full Release solution build: passed, 0 warnings, 0 errors; see `release-build.log` and `release-build-results.json`.
- Candidate qualification suite: 337 passed, 0 failed, 0 skipped; see `qualification-tests.log`.
- Baseline: 310 qualification tests passed, 16 Stage 4B tests passed, and Release build passed with 0 warnings/errors.
- No skips were introduced. Tests were run on Windows x64/.NET 10; the full application solution builds its configured target frameworks. No live provider tests were run.

## I. Remaining qualifications

The registry does not detect restoration of an old SQLite snapshot or cloning of an authority environment. Network filesystems and multi-host SQLite access are not qualified. The host clock is trusted, and wall-clock rollback protection is not implemented. On Windows the operator must secure the registry directory ACLs; Unix provisioning sets the database file to user read/write. The 81-file closure does not hash the system runtime, OS, kernel, or external runtime components, and a narrow hash-to-use race remains. Cross-platform canonical vectors have not yet been executed on Linux/macOS. Provider-side routing/retries and exact model execution count remain outside Baize's view.

## J. Final disposition

**READY FOR INDEPENDENT R1 QUALIFICATION.** Implementation and evidence meet the bounded R1 handoff target. This is not an independent review acceptance and is not authorization to execute live.

## K. Next owner decision

Assign independent R1 verification against the implementation commit and committed evidence. The reviewer should reproduce the closure and plan, audit F-01 ordering/atomicity and the negative-test matrix, and verify the unchanged profile/budget. After independent acceptance, the owner must separately decide whether to request a new live proposal/authorization. This handoff itself authorizes no live request.

## Evidence index

- [Independent review handoff](reviews/bq1b-stage4b-r1-evidence/independent-review-handoff.md)
- [Artifact binding](reviews/bq1b-stage4b-r1-evidence/artifact-binding.json)
- [Deployment closure manifest](reviews/bq1b-stage4b-r1-evidence/deployment-closure-manifest.json)
- [Candidate plan and identity](reviews/bq1b-stage4b-r1-evidence/candidate-plan/plan.json)
- [Review-only proposal](reviews/bq1b-stage4b-r1-evidence/proposal-manifest.json)
- [Proposal validation](reviews/bq1b-stage4b-r1-evidence/proposal-validation.json)
- [Canonical vectors](reviews/bq1b-stage4b-r1-evidence/canonical-evidence-vectors.json)
- [Finding matrix](reviews/bq1b-stage4b-r1-evidence/findings-matrix.json)
- [Stale keep-alive interpretation](reviews/bq1b-stage4b-r1-evidence/stale-keepalive-interpretation.md)
