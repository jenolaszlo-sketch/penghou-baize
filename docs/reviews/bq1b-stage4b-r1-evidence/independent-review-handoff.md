# Independent R1 qualification handoff

## Review request

Independently verify the R1 implementation against baseline `6e99d976a4e521c62b049983281606d04a03a96e` and review commit `e4f8e132c10f199a1754ae629ea95ceac19c0255`. Implementation commit: `85c1f63f6c785fefe10e311df67a6370ee7e462e`. The evidence package and final disposition are recorded in the companion Stage4B R1 handoff.

Review F-01 first: inspect schema 2.0 identity/signature binding, registry provisioning and authority binding, immediate SQLite transaction and unique keys, checks before and after claim, HTTP admission check, live resume denial, failure behavior, and the barrier subprocess/restart/crash tests. Confirm that no execution path can dispatch before a durable claim and that terminal state cannot release it.

Then independently reproduce the artifact closure and plan identities from the committed Release build; inspect canonical evidence vectors and byte-level no-BOM test; compare transport and budget values with the historical accepted profile; inspect the changed-file inventory; and verify the complete test log/build log and Git binding.

## Proposed live candidate

The candidate plan is `candidate-plan/plan.json`, hash `sha256:971ea862f91ccb6166e2b36402173d0dce262aa992ad72c9a204e548d43a6451`. Proposal `proposal-manifest.json` has digest `sha256:5f0a7638799e4aaf8e1be4236fdf5e01207bec0d10e19c5fab1cea889a50f41`. Its status is pending independent review and owner acceptance. It records `authorizationIssued=false`, `executionAuthorized=false`, and `qualificationPerformed=false`. The plan remains live-blocked on economics evidence, protocol revision evidence, token-bound enforcement, mutable model revision, and explicit operator authorization. No private signing key or live authorization is present in this repository.

## Review boundary

This package requests independent R1 qualification only. No live provider request has been made, no provider credential was read, no authorization was issued, and no credits were spent. Independent acceptance and a separate owner decision are still required before any future live execution proposal can proceed.

## Known verification limits

The suite was executed on the Windows x64/.NET 10 candidate host only; cross-platform evidence canonicalization vectors are checked in but have not yet been executed on Linux or macOS. SQLite snapshot rollback/cloning and network filesystems are outside the guarantee. The system clock is trusted. Deployment hashes cover the 81-file application closure, not the OS/runtime/kernel, and a small hash-to-use race remains. Credential readiness proves nonempty availability only, not provider validity or account allowance. Provider-side routing, retry, and execution counts remain unknown.
