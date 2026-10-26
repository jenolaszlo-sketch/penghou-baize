# BQ-1b Stage 4B handoff — first real API qualification preparation

Date: 2026-10-26 (Asia/Manila). Repository: `jenolaszlo-sketch/penghou-baize`.

**Disposition: READY FOR AUTHORIZED LIVE QUALIFICATION.** Gate A confirms the
Stage 4A physical-attempt bound of one for the selected HTTP/1.1 profile. A
fail-closed external-network interlock is implemented and verified. One
OpenRouter/Llama candidate is fully prepared, and `validate-live` accepts its
exact proposed manifest offline without creating a run directory or contacting a
provider. No live provider request, credential read or qualification run was
performed.

| Binding | Value |
| --- | --- |
| Base (Stage 4A accepted) | `f9ffa02adec676af73a0220761e64f84fdc94826` |
| Implementation commit | `214a363` (live authorization interlock, candidate fixtures, tests, Gate A stale-connection probe) |
| Dedicated worktree | `Penghou.Baize.stage4b` on branch `implementation/baize-bq1b-stage4b` |
| Final documentation commit | the commit containing this handoff (see `git log -1`) |
| Transport audit | [Stage 4A transport audit](model-qualification-bq1b-stage4a-transport-audit.md) |
| Stage 4B evidence | [bq1b-stage4b-evidence](reviews/bq1b-stage4b-evidence/README.md) |
| Proposed manifest | [model-qualification-bq1b-stage4b-proposed-live-manifest.json](model-qualification-bq1b-stage4b-proposed-live-manifest.json) |
| Candidate fixtures | `eng/qualification/stage4b/` |
| Runtime | .NET SDK 10.0.401; Microsoft.NETCore.App 10.0.12; `System.Net.Http.dll` `b98ed0cb...f066801` |
| Tool packages | `Penghou.Zhinu.Sqlite 0.2.0-preview.4`, `Penghou.Siming.Sqlite 1.0.0`, `Microsoft.Extensions.Configuration.Json 10.0.9` |
| Production packages | unchanged; `git diff -- src/` is empty |

## Gate A — transport boundary re-check

The Stage 4A reviewed profile remains exactly
`baize-sockets-http11-exact-v1`: HTTP/1.1 exact, pinned `SocketsHttpHandler`, no
redirects, no configured authentication, no cookies, no proxy, no decompression,
no retry middleware, one connection per server, one physical-attempt bound.

The focused re-check added a stale keep-alive connection probe: the controlled
server completes a first request with `Connection: keep-alive`, then closes the
connection; a second independently admitted request succeeds and the server
observes exactly two complete requests across two connections. The runtime
source reasoning from Stage 4A is unchanged: `HttpConnection._canRetry` is false
for a POST with content and no `Expect: 100-continue`; the
`MaxConnectionFailureRetries` loop, version downgrade and HTTP/2 stream-limit
replay branches are unreachable for this profile. A TCP/TLS reconnection is not
another HTTP request. The bound of one per admitted invocation remains
defensible. No blocker was removed to enable this stage.

## Fail-closed external-network interlock

`run` is now synthetic-only. It still parses `--allow-live` for compatibility,
but any non-synthetic selection is refused with
`live-execution-requires-run-live-command` before a run directory, eligibility
assessment, credential read or dispatch. Synthetic runs remain local and
explicitly labeled as not live-authorized.

A real run requires the separate `run-live` command and a separate
`qualification-live-authorization` file. The authorization is bound to:

- exact plan, contract-registry, inventory and policy hashes;
- exact transport profile id and identity;
- provider, endpoint id, base URL, origin, model, protocol and API version;
- effective settings and credential reference (name only, never value);
- exact case and trial ids, semantic and physical-attempt ceilings;
- maximum model calls, HTTP requests, input/output/reasoning/total tokens, cost
  and currency;
- exact artifact destination;
- authorization reference, operator reference, issued time and expiry.

`run-live` refuses to create a run directory if any binding is missing or
different. `resume` of a live run requires the same authorization, re-validates
it against the manifest and current eligibility, and never replenishes budget.
`validate-live` performs the complete offline validation, prints the proposed
cohort and liability, and creates no artifacts. `--allow-live` alone can no
longer reach a real provider.

## Selected first real target

| Field | Value |
| --- | --- |
| Hosting provider | OpenRouter |
| API protocol / version | `openai-chat-completions`, `v1` |
| Adapter | existing `Penghou.Baize.OpenAi`, assembly version 0.3.0.0 |
| Endpoint | `https://openrouter.ai/api/v1` |
| Provider model | `meta-llama/llama-3.1-8b-instruct` |
| Revision | unknown; mutable alias recorded explicitly |
| Settings | `Dialect=Standard`; no thinking/reasoning mode configured |
| Contracts | `chat.baseline@3` plus shared `chat.streaming-integrity@2` |
| Native tools | not selected; not claimed |
| Transport | `baize-sockets-http11-exact-v1`, bound 1 |
| Attempts | one case, one trial, one semantic invocation, one physical HTTP attempt |
| Token maximum | 256 input, 512 output, 0 reasoning, 768 total |
| Cost maximum | **0.0004 USD** (reviewed candidate source `openrouter-pricing-review-1`) |
| Credential reference | `OPENROUTER_API_KEY` (value never read during preparation) |
| Artifact destination | `artifacts/qualification/stage4b-openrouter-llama31-8b-smoke` |

`chat.streaming-integrity@2` observes the `chat.baseline@3` execution and does
not create a second request. One successful smoke is not a model-wide
qualification; it is the first compatibility observation for this exact
configuration. The operator must confirm the current provider price, account
allowance and currency before issuing the authorization. If they differ from the
proposal, do not issue the authorization; regenerate the plan.

## Proposed authorized invocation

After a separate operator decision and an exact authorization file, the command
would be:

```powershell
dotnet eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll run-live `
  --plan artifacts\qualification\stage4b-preparation\plan-candidate\plan.json `
  --authorization <exact-qualification-live-authorization.json> `
  --out artifacts\qualification\stage4b-openrouter-llama31-8b-smoke
```

This command was **not** executed. The proposed manifest is a non-executable
`qualification-live-run-proposal`; its nested template is not an authorization
until the operator replaces the placeholders and confirms the external facts.

## Verification

- Release build: 0 warnings / 0 errors.
- Qualification project: 309 deterministic tests passed.
- Full solution: see the evidence bundle; all deterministic suites passed.
- `validate-live` accepted the exact candidate with authorization hash
  `sha256:1f50cb40fae38a5b5321ea5181f91cff1ab97404b625925c4363cfd802680dc6`,
  plan hash `sha256:d0b52fec3c97e26f80a93579c1717c3bbdec97c71bad2ebdbc567da27741f44f`,
  and did not create the run directory.
- Authorization denial tests cover missing, mismatched plan/endpoint/profile/
  selection/budget/output/expiry, synthetic and loopback profiles, and live
  resume without authorization.
- No private prompt, credential or tool argument is persisted; the candidate
  authorization contains only the credential slot name.
- Existing Stage 1–4A tests and synthetic contracts remain passing.

## Remaining restrictions

- Only OpenRouter `meta-llama/llama-3.1-8b-instruct` over the existing OpenAI
  adapter is proposed; no other provider/model is authorized.
- Only `chat.baseline@3` and shared streaming observation are selected. Tools,
  JSON Schema, reasoning-mode and multi-turn contracts are not in this stage.
- The model revision is unknown and the alias is mutable.
- Provider pricing and account allowance require operator confirmation.
- HTTP/2, HTTP/3, proxy, redirect, configured authentication and custom-handler
  paths remain unsupported.
- No automatic retry, model switching, cohort expansion, production profile
  update, merge, push or package publication is authorized.
- The Stage 4A process deviation remains disclosed: an intermediate test
  previously attempted an OpenRouter request with a sentinel credential. The
  final Stage 4B commands and tests make no live provider request.

## Next step

The operator must review the proposed manifest, confirm the current price,
allowance and credential availability, issue a separate exact
`qualification-live-authorization` file, and explicitly authorize one live run.
Then `run-live` may execute the single authorized cohort. No Stage 4B live run,
Stage 5 handoff or broader model cohort begins automatically.
