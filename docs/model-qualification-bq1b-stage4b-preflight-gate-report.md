# BQ-1b Stage 4B pre-execution gate report

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.
Qualification branch: `implementation/baize-bq1b-stage4b-live`.
Accepted baseline: `acc539738512f6c79900e0eecf36c8ca63fb5095`.

**Disposition: BLOCKED BEFORE LIVE EXECUTION.** Gate A, C and E pass. Gate B
passes on pricing but blocks on unconfirmed account allowance. Gate D blocks
because `OPENROUTER_API_KEY` is absent from the approved process environment.
No authorization was issued or consumed and no live provider request was made.

## Gate A — Git and artifact integrity: PASS

| Check | Result |
| --- | --- |
| Accepted preparation commit exists | `acc5397` on `implementation/baize-bq1b-stage4b` |
| Tree / parent | `788bd4a1...` / `edbe6bb7...` |
| Ancestry | `f9ffa02` -> `214a363` -> `edbe6bb` -> `acc5397` |
| Accepted worktree clean | Yes; preserved unchanged |
| Evidence files present | Handoff, Stage 4B evidence README, proposed manifest |
| Proposed manifest matches approved candidate | Yes; provider, model, adapter, contracts, one attempt, token limits and USD 0.0004 all match |
| Production source diff since Stage 4A | Empty (`git diff f9ffa02..acc5397 -- src/`) |
| Changed-file classes | docs, `eng/Penghou.Baize.Qualification`, qualification tests, stage4b fixtures |

**Build binding note.** The accepted proposal's plan hash
`sha256:d0b52fec3c97e26f80a93579c1717c3bbdec97c71bad2ebdbc567da27741f44f`
is bound to the accepted implementation build `214a363`. The docs-only commits
`edbe6bb` and `acc5397` do not change source, but a fresh build at `acc5397`
changes the assembly informational source revision and therefore produces a
different plan hash. The exact live run must use the accepted `214a363` build
and the accepted plan; the isolated evidence branch is based on `acc5397` for
documentation only. This is recorded rather than silently rebuilding a different
manifest.

The exact proposal SHA-256 is
`f6d4fbc1049c34e823d4e246b63c4ef22bd6b05419ee4f47f9f6b9a2b79a3c1d`.

## Gate B — Provider availability, pricing and allowance: BLOCKED

Pricing and availability pass. Allowance is unconfirmed.

Public OpenRouter metadata at `2026-10-26T03:37:12Z`:

- model `meta-llama/llama-3.1-8b-instruct` is present;
- the OpenAI-compatible `/api/v1/chat/completions` route is supported;
- five endpoints are listed, all with status 0;
- no per-request or minimum charge is exposed in the endpoint metadata;
- the model is not a reasoning model and the approved reasoning budget is 0;
- OpenRouter states that inference pricing is passed through without markup;
  credit-purchase fees are separate from inference usage.

Conservative maximum using the highest listed endpoint prices, not the
cheapest endpoint:

| Component | Highest listed per-token price | Approved units | Maximum charge |
| --- | --- | --- | --- |
| Input | 0.00000022 USD (CoreWeave) | 256 | 0.00005632 USD |
| Output | 0.000000287 USD (Cloudflare) | 512 | 0.000146944 USD |
| Reasoning | not priced / 0 | 0 | 0 |
| **Total inference** | | | **0.000203264 USD** |

The ceiling is **0.0004 USD**, leaving **0.000196736 USD** headroom. The
calculation uses the maximum across all currently listed endpoints rather than
assuming the cheapest route, and does not assume that reported total tokens are
the only billable component. The raw public metadata is retained under the
ignored preflight artifacts; the sanitized calculation is
[pricing-observation.json](reviews/bq1b-stage4b-live-preflight/pricing-observation.json).

Account allowance could not be confirmed without the provider credential.
Because a sufficient allowance is a required pass condition, Gate B is blocked
even though the price bound is safely below the ceiling.

## Gate C — Authorization interlock: PASS

The existing interlock was re-verified independently in the isolated worktree:

- `run` is synthetic-only; `--allow-live` on a real profile is refused with
  `live-execution-requires-run-live-command`.
- 16 Stage 4B interlock tests passed in Release.
- `validate-live` accepted the exact proposal using the accepted `214a363`
  build and plan. Exit 0, `kind=live-authorization-validated`,
  `executionAuthorized=false`, no run directory created.
- Test authorization was deleted after validation; no usable authorization
  remains.
- The offline validation output is retained at
  [validate-live-output.json](reviews/bq1b-stage4b-live-preflight/validate-live-output.json)
  with SHA-256 `df1cfb19171ca90e9ec68a9061cfbbe92fbb83d817360425869a6c22f269ee2e`.
- Mismatched, expired, reused, synthetic and loopback paths remain covered by
  the accepted Stage 4B denial tests.

## Gate D — Credential isolation: BLOCKED

The approved mechanism for the candidate is the process environment variable
`OPENROUTER_API_KEY`, resolved only inside a live attempt lease. A presence-only
check returned **absent**. The credential value was not read, printed, logged or
substituted. No alternative account or secret source was used.

Without the credential, live authorization cannot be issued and account
allowance cannot be checked. This gate is a hard blocker.

## Gate E — Transport and budget invariants: PASS

- Transport profile: `baize-sockets-http11-exact-v1`.
- Profile identity:
  `sha256:88486795d1c6630425c34e0e3cf4c60fafda5af185928b8e759257f904930adb`.
- Physical-attempt upper bound: 1, enforced at the guard and reflected in the
  budget reservation.
- HTTP/1.1 exact; pinned `SocketsHttpHandler`; no redirects, authentication
  negotiation, cookies, proxy, decompression or retry middleware.
- Stale keep-alive probe passed in Release
  (`StaleKeepAliveConnection_RecoversWithoutReplay`): two admitted attempts,
  two complete requests, two connections, no third request.
- No existing Stage 4A restriction was removed.

## Authorization and execution

- Exact authorized manifest: **none issued**. The proposed manifest remains a
  candidate.
- Authorization consumed: **no**.
- Live provider request: **no**.
- Physical attempts: **0**.
- Terminal result: **not-run**.
- The test-only offline authorization used by `validate-live` was deleted.

## Financial accounting

| Field | Value |
| --- | --- |
| Proposed maximum liability | 0.0004 USD |
| Conservative inference calculation | 0.000203264 USD |
| Reserved liability | 0 (no run started) |
| Reported provider usage | none |
| Observed/estimated charge | 0 (absence of a run, not a free-run claim) |
| Budget state | not-started |
| Authorization ceiling exceeded | no; no dispatch occurred |

## Workload results

`chat.baseline@3` and `chat.streaming-integrity@2` were **not executed against a
provider**. Existing offline synthetic qualification behavior is unchanged.
No live assertion, streaming-integrity or usage result is claimed.

## Independent review handoff

An independent reviewer can verify the accepted baseline, manifest digest,
pricing observation, interlock tests, offline validation output, Gate D
credential-presence result and the absence of live execution. The live
transport, workload and financial results remain unproven because no provider
request occurred.

The Stage 4A sentinel-request disclosure remains part of the record. This
preflight did not repeat it.

## Next action

To proceed, the operator must:

1. make `OPENROUTER_API_KEY` available through the approved process-environment
   mechanism without printing it;
2. confirm the account has sufficient allowance for the USD 0.0004 ceiling;
3. explicitly approve the exact proposed manifest for one live run.

After those steps, a fresh exact authorization can be issued and a single
`run-live` may be performed with the accepted `214a363` build/plan. No Stage 5
work, retry, model substitution or scope expansion is authorized.
