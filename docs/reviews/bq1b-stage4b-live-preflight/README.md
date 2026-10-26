# BQ-1b Stage 4B live-preflight evidence

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Branch: `implementation/baize-bq1b-stage4b-live`.
Accepted baseline: `acc539738512f6c79900e0eecf36c8ca63fb5095`.
Owner decision: **STAGE 4B PREPARATION ACCEPTED WITH QUALIFICATIONS**.

**Result: BLOCKED BEFORE LIVE EXECUTION.** No live authorization was issued or
consumed, no provider request was dispatched, and no credential value was read.
The blocker is Gate D (missing `OPENROUTER_API_KEY`) and the consequent
unconfirmed account allowance. Pricing is within the approved ceiling.

## Gate summary

| Gate | Result |
| --- | --- |
| A — Git and artifact integrity | PASS |
| B — Pricing / availability / allowance | BLOCKED (pricing PASS, allowance unconfirmed) |
| C — Authorization interlock | PASS |
| D — Credential isolation | BLOCKED (credential absent) |
| E — Transport and budget invariants | PASS |

## Preflight commands

```powershell
# Accepted worktree: verify baseline and preserve it.
git -C C:\Users\Laszlos\source\repos\Penghou.Baize.stage4b status --short
git -C C:\Users\Laszlos\source\repos\Penghou.Baize.stage4b rev-parse HEAD
git -C C:\Users\Laszlos\source\repos\Penghou.Baize.stage4b diff --stat f9ffa02..acc5397 -- src/

# Isolated worktree: build the runner and run the interlock suite.
dotnet build eng\Penghou.Baize.Qualification\Penghou.Baize.Qualification.csproj -c Release
dotnet test tests\Penghou.Baize.Qualification.Tests\Penghou.Baize.Qualification.Tests.csproj -c Release --filter "FullyQualifiedName~Stage4b"

# Validate the exact accepted proposal with the accepted 214a363 build/plan.
# A test-only authorization was generated, used once, and deleted.
dotnet <accepted 214a363 runner>\Penghou.Baize.Qualification.dll validate-live `
  --plan <accepted plan>\plan.json `
  --authorization <test-only authorization> `
  --out <preflight run-not-created>

# Presence-only credential check; no value read.
Test-Path Env:OPENROUTER_API_KEY
```

The accepted worktree was not modified. The isolated worktree contains the
documentation and evidence. The accepted plan is copied under the ignored
`artifacts/qualification/stage4b-live-preflight/accepted-plan/` directory for
local preflight use, but is not committed.

## Pricing evidence

Public sources observed at `2026-10-26T03:37:12Z`:

- `https://openrouter.ai/api/v1/models`
- `https://openrouter.ai/api/v1/models/meta-llama/llama-3.1-8b-instruct/endpoints`
- `https://openrouter.ai/docs/faq`

The model is available, the chat-completions route is supported and five
endpoints are listed. The conservative maximum uses the highest listed per-token
prices, not the cheapest endpoint:

```text
input:  256 * 0.00000022  = 0.00005632 USD
output: 512 * 0.000000287 = 0.000146944 USD
reasoning: 0
total:                      0.000203264 USD
ceiling:                    0.000400000 USD
headroom:                   0.000196736 USD
```

No per-request or minimum charge was present in the public endpoint metadata.
OpenRouter states that inference pricing is passed through without markup;
credit-purchase fees are separate from the inference charge. Account allowance
could not be confirmed without the credential.

Sanitized machine-readable evidence:
[pricing-observation.json](pricing-observation.json).

## Authorization validation

`validate-live` accepted the exact proposed plan/cohort using the accepted
`214a363` build and plan. It exited 0 with
`kind=live-authorization-validated`, `executionAuthorized=false`,
`qualificationPerformed=false`, and created no run directory. The test-only
authorization was deleted immediately. The output is retained at
[validate-live-output.json](validate-live-output.json).

The accepted proposal is
`docs/model-qualification-bq1b-stage4b-proposed-live-manifest.json`. Its
canonical sorted-key UTF-8 JSON digest is
`sha256:8cdbef06a86722625e1581e510bb630f3e3f397b870a67a44436dee7ddfc7253`;
the raw file-byte SHA varies with Git checkout line endings and is not used as
the execution binding. The authoritative plan hash is
`sha256:d0b52fec3c97e26f80a93579c1717c3bbdec97c71bad2ebdbc567da27741f44f`.

## Transport and workload status

- Transport profile `baize-sockets-http11-exact-v1` is unchanged; identity
  `sha256:88486795d1c6630425c34e0e3cf4c60fafda5af185928b8e759257f904930adb`.
- Physical-attempt bound 1 is unchanged.
- Stale keep-alive test passed in Release.
- `chat.baseline@3` and `chat.streaming-integrity@2` were **not executed against
  a provider**.
- No provider usage, streaming integrity, tool-call or model capability result
  is claimed.

## Financial accounting

| Field | Value |
| --- | --- |
| Maximum authorized liability | 0.0004 USD (proposed; no authorization issued) |
| Reserved liability | 0 |
| Provider usage | none |
| Observed/estimated charge | 0 (no run; not a free-run claim) |
| Budget state | not-started |

## Artifact hashes

| File | SHA-256 |
| --- | --- |
| `docs/model-qualification-bq1b-stage4b-owner-acceptance.md` | `2f2e47e93f4173b815977cd60b8c9756bd5f0872c1d82319006652fa7e94400e` |
| `docs/model-qualification-bq1b-stage4b-preflight-gate-report.md` | `ec27c09752447dfcd490f3d71c438075eed500b90c84418e17cb7bb489979782` |
| `pricing-observation.json` | `f68f59391202d06f32f998bf51db94a33aa6a1c0b88b348671ce2437b896328c` |
| `preflight-evidence.json` | `c553226f8325eecf46b99f79576bca7bf034247392c305c2334c5bfb6d511795` |
| `validate-live-output.json` | `df1cfb19171ca90e9ec68a9061cfbbe92fbb83d817360425869a6c22f269ee2e` |
| `docs/model-qualification-bq1b-stage4b-live-preflight-review-handoff.md` | `357e89252e63e37be3f6340c94867482c14fd608d9e1884c1bb4a37b7643c25a` |

The hashes above are file hashes in this worktree; they are not a substitute
for the plan's canonical hash or the authorization's canonical binding.

## Independent review

The reviewer can verify the accepted baseline, proposal digest, pricing
calculation, interlock tests, offline validation and credential-absence result.
The live transport, workload and financial observations are not available
because no live execution occurred. The Stage 4A sentinel-request disclosure
remains part of the record.

## Next action

Provide `OPENROUTER_API_KEY` through the approved environment mechanism, confirm
the account allowance, and explicitly approve the exact manifest. Then issue a
fresh exact authorization and perform at most one `run-live` using the accepted
`214a363` build/plan. No retry, substitution, Stage 5 work, push, merge or
publication is authorized.
