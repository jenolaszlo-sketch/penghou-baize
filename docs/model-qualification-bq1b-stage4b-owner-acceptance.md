# BQ-1b Stage 4B owner acceptance

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

## Owner decision

**STAGE 4B PREPARATION ACCEPTED WITH QUALIFICATIONS.**

The owner accepted the Stage 4B preparation baseline and authorized progression
toward one bounded live qualification, subject to the pre-execution gates in the
Stage 4B authorization. This acceptance is not a live qualification result, a
general provider approval, a model capability claim, or authorization to start
Stage 5.

## Accepted baseline

| Binding | Value |
| --- | --- |
| Branch | `implementation/baize-bq1b-stage4b` |
| Commit | `acc539738512f6c79900e0eecf36c8ca63fb5095` |
| Tree | `788bd4a1734deef0b853fb6254bea1aba0ae422a` |
| Parent | `edbe6bb708e18a05cda7163b8fcd6d31f03d56de` |
| Worktree | `C:/Users/Laszlos/source/repos/Penghou.Baize.stage4b` (clean) |
| Implementation commit | `214a363a7e3ffe60325016ebd986427979687840` |
| Stage 4A base | `f9ffa02adec676af73a0220761e64f84fdc94826` |
| Production source diff since Stage 4A | empty |

Accepted evidence:

- `docs/model-qualification-bq1b-stage4b-handoff.md`
- `docs/reviews/bq1b-stage4b-evidence/README.md`
- `docs/model-qualification-bq1b-stage4b-proposed-live-manifest.json`

The Stage 4A sentinel-request disclosure remains part of the qualification
record. No live provider request, credential value read, push or merge occurred
during preparation. The previously reported full-solution result was 2,122
passed, 28 skipped and 0 failed; the qualification project had 309 passing
tests.

## Authorized scope, subject to gates

One live qualification of the exact proposed candidate is contemplated:

- OpenRouter `meta-llama/llama-3.1-8b-instruct`
- existing Baize OpenAI-compatible adapter
- `chat.baseline@3` plus shared `chat.streaming-integrity@2`
- one physical HTTP attempt under `baize-sockets-http11-exact-v1`
- 256 input / 512 output / 0 reasoning / 768 total tokens
- maximum inference liability USD 0.0004

The proposed manifest is a proposal, not an authorization. The exact live run
requires a separately issued `qualification-live-authorization` after all
pre-execution gates pass and after explicit operator approval of the final
manifest.

## Current status

The preflight gate report is
[model-qualification-bq1b-stage4b-preflight-gate-report.md](model-qualification-bq1b-stage4b-preflight-gate-report.md).
The current disposition is **BLOCKED BEFORE LIVE EXECUTION** because the provider
credential is absent and account allowance is consequently unconfirmed. No
authorization was issued or consumed, and no live request was dispatched.
