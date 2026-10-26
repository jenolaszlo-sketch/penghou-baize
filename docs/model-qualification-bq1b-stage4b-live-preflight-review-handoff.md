# BQ-1b Stage 4B live-preflight independent-review handoff

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Branch: `implementation/baize-bq1b-stage4b-live`.
Accepted baseline: `acc539738512f6c79900e0eecf36c8ca63fb5095`.
Status: **BLOCKED BEFORE LIVE EXECUTION.**

## What the reviewer should verify

1. The accepted baseline exists and the accepted worktree is clean.
2. The live branch is based on the accepted commit and contains only
   documentation/evidence changes.
3. The proposed manifest matches the owner-approved candidate: provider, model,
   adapter, contracts, one attempt, token ceilings and USD 0.0004 ceiling.
4. The accepted plan hash is bound to the accepted implementation build
   `214a363`, not to a rebuild at the docs-only commit.
5. Public pricing evidence was retrieved at the recorded time and the
   conservative maximum (0.000203264 USD) is below the ceiling.
6. Account allowance is unconfirmed because the credential is absent.
7. `run` is synthetic-only, `--allow-live` is insufficient, `run-live` requires
   exact authorization, `validate-live` is offline and live `resume` requires
   authorization.
8. The offline validation used the accepted build/plan, exited 0, created no run
   directory and left no usable test authorization.
9. The transport profile and physical-attempt bound are unchanged, and the
   stale keep-alive probe passes.
10. No live provider request, credential value read, reservation or charge
    occurred.

## What is established

- The Stage 4B preparation and its interlock are accepted.
- The exact candidate manifest is consistent with the approved candidate.
- Public pricing supports a conservative maximum under the ceiling.
- The authorization interlock and transport profile remain intact.
- The current blocker is operational: the provider credential is absent and
  account allowance is therefore unconfirmed.

## What is not established

- Model availability at execution time (public metadata is not a reservation).
- A real provider's response, streaming behavior or usage reporting.
- The actual one-attempt live execution and its financial settlement.
- Any model capability beyond the selected contracts.
- Any generalization to other models, providers, endpoints or transports.

## Evidence

- [Owner acceptance](../model-qualification-bq1b-stage4b-owner-acceptance.md)
- [Preflight gate report](../model-qualification-bq1b-stage4b-preflight-gate-report.md)
- [Preflight evidence bundle](bq1b-stage4b-live-preflight/README.md)
- [Proposed manifest](../model-qualification-bq1b-stage4b-proposed-live-manifest.json)

The Stage 4A sentinel-request disclosure remains part of the qualification
record. This preflight did not repeat it.

## Required decision

Before any live dispatch, the operator must make `OPENROUTER_API_KEY` available
through the approved mechanism, confirm allowance, and explicitly approve the
exact manifest. A fresh exact authorization must then be issued. Until that
happens, the disposition remains **BLOCKED BEFORE LIVE EXECUTION** and no Stage 5
work may begin.
