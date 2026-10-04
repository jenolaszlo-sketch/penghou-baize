# Governable model and HTTP access: implementation handoff

Updated 2026-10-04. Current gate: [shared transport release checkpoint](../../Penghou/docs/model-http-release-handoff.md).
The new contract code is in Penghou; remote CI/publication and indexed-package
verification precede Baize GM-2/3/4. No Baize migration or Hufu adapter is claimed.

Read [the canonical implementation roadmap](governable-model-http-plan.md) and
[verbatim specification](proposals/2026-10-04-governable-model-http.txt) first.
Then read [source inventory](governable-model-http-inventory.md),
[scope](scope-and-boundaries.md), [streaming regression contract](roadmap-streaming-integrity.md),
[generation roadmap](roadmap-generation-client.md),
[resource boundary notes](roadmap-resource-boundaries.md),
[shared contract queue](../../Penghou/ROADMAP.md) and
[Hufu's separate follow-up queue](../../Penghou.Hufu/docs/roadmap.md).

Inspect `git status` before editing. At planning time Baize already had changes
to README and experience-signals documentation plus an untracked resource
roadmap. Preserve those changes; they are not a clean baseline to overwrite or
bulk-stage. Do not load `.env.local` or call real providers to review transport.

## Source review starting points

- `src/Penghou.Baize/LlmClientBase.cs` owns streaming HTTP dispatch and auth
  application; an injected HttpClient factory is not a semantic model boundary.
- `src/Penghou.Baize/ILlmClient.cs`, `ILlmCompletionClient.cs`,
  `IBaizeBatchClient.cs` and `Generation/IGenerationClient.cs` define distinct
  existing lifecycles. Inspect provider implementations as well as the base class.
- `src/Penghou.Baize/LlmRequest.cs` already carries host-neutral metadata that
  must not be serialized onto provider wire requests. Preserve that behavior.
- `src/Penghou.Baize/BaizeHttp.cs` and DI registration own the named HTTP client
  and timeout behavior; preserve diagnostics, handlers and provider test fixtures.
- `src/Penghou.Baize.Router` owns retry/fallback selection; inspect generation
  executors, native provider retries and schema-repair paths separately.
- `src/Penghou.Baize/LlmClientFailureKind.cs` already distinguishes provider
  authorization from availability. Add neutral host denial mapping deliberately
  rather than treating every 403 or exception as the same decision.
- `../Penghou/src/Penghou.IO.Abstractions/Contracts.cs` defines the existing
  credential-free web reader. Inventory its type closure; do not replace it with
  a general authenticated HTTP transport or break published IO contracts.

## Resume prompt

> Resume from Penghou docs/model-http-release-handoff.md. GM-0 decisions and
> GM-1 contract source/local qualification are recorded there; complete pending
> remote CI, publication and public-package verification gates
> before Baize adoption. Do not reimplement the new contracts or restart the
> completed sandbox-parent experiment. After indexed publication, pin Baize to
> the exact Penghou.Model.Abstractions and Penghou.Http.Abstractions versions
> and execute GM-2/3/4, B1-B12, then GM-5/6. Preserve all consumer lifecycles,
> context/usage, default behavior, provider diagnostics and source compatibility.
> Snapshot/validate supported typed payloads before policy and dispatch; move
> governed credential resolution after semantic admission; no built-in upload,
> result, retry or repair route may bypass the seam. Authority denial stops
> fallback by default. Keep Baize independent of Hufu. Hufu adapters, vaulting,
> budgets and network enforcement are separately owned follow-ups. Use Luna
> for bounded subtasks where available and Terra if offered as a fallback.
