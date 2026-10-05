# Governable model and HTTP access: implementation handoff

Updated 2026-10-05. GM-1P is verified: both neutral packages are indexed at
**0.1.0-preview.1**, Penghou CI run 37240540643 and publication run 37243499710
succeeded. Baize source now pins those exact versions without a Hufu dependency.

Read the [canonical roadmap](governable-model-http-plan.md),
[composition/profile guide](governable-transports.md),
[verbatim specification](proposals/2026-10-04-governable-model-http.txt) and
[call inventory](governable-model-http-inventory.md). Existing streaming,
generation, resource-boundary and experience roadmaps remain regression contracts.

## Delivered chat-first milestone

GM-2 chat-first: semantic streaming/completion seams for built-in chat clients,
actual endpoint identity, bounded immutable request snapshots, neutral context
and usage preservation. Router admission precedes credential resolution;
authority denial stops fallback and each availability attempt is checked again.
Extensions.AI forwards typed context outside wire metadata. Built-in chat,
batch and generation base HTTP paths use the injected neutral HTTP seam with
owned response bodies and deadlines through body consumption.

Existing constructors remain; defaults work without Hufu. All 12 source project
closures resolve public neutral packages. Batch/generation semantic authorization
and large upload qualification remain open; HTTP bridging alone does not close
GM-3. Opaque custom providers need explicit qualification.

## Working-tree precautions and evidence

Inspect status before editing. The existing change to
`docs/roadmap-experience-signals.md` belongs to prior work; preserve it and avoid
bulk staging. Base revision is da6e6ceee1c67822fdd564442117c28ec57ce90c.
This milestone is published as Baize 0.3.0-preview.7 from
4a220ed7a76ca8a98bb59b63a47b709e2e16d76c. All 12 package indexes include this
version; CI and publication succeeded. The .NET 9 SDK omission in the initial
macOS matrix was corrected before release qualification.
Do not load `.env.local` or call paid providers for offline qualification.

Qualification evidence is recorded below. Development CI consumer checks pack
a temporary Baize candidate with public neutral dependencies. Post-publication
checks additionally restored the published Baize packages from public NuGet only,
with fresh caches and no local feed.

## Next gate / resume prompt

> Continue GM-3 from the canonical roadmap and call inventory. Do not repeat
> GM-0/1, package publication or the completed sandbox-parent experiment.
> Keep the exact published neutral dependencies and Baize independent of Hufu.
> Add typed semantic transport profiles for built-in batch/generation operations,
> per-item context and status/result/cancel handles, then uploads and asset reads.
> Preserve wire protocols, diagnostics, retries, cancellation and constructor
> compatibility. Bind handles to target and authenticated context; a returned
> handle is not permission. Every consequential operation and retry needs a fresh
> check, with credentials resolved after admission. Qualify large upload memory
> and ownership before widening the supported finite-buffer profile. Complete
> remaining GM-4 executor/custom-provider closure and B1-B12 before claiming
> full Baize governance or releasing adapters. Hufu policy/budgets and physical
> isolation remain separately owned. Use Luna for bounded subtasks where available.

## Local qualification (2026-10-05)

- Full solution Release build: zero warnings/errors; additive public API baselines pass.
- All 12 test projects: 871 passed and 14 pre-existing skips per net8.0/net10.0;
  Tools additionally passes 71 tests on net9.0. No paid/live-provider calls.
- All 12 existing line/branch coverage gates pass without lowering thresholds.
  Core coverage is 87.46% line / 82.19% branch against the 80% gate.
- All 12 Baize packages pack successfully. Both fresh core package consumers
  restore neutral dependencies from public NuGet and pass default HTTP ownership
  and model-denial-before-dispatch checks.
- Exact dependencies/no Hufu closure checks and PowerShell parser checks pass.
- CI now includes focused adoption/consumer checks on Windows, Ubuntu and macOS;
  all remote jobs passed in CI run 37251898339.

See [machine-readable evidence and open gates](governable-transports-qualification.json).
`dotnet format Penghou.Baize.slnx --verify-no-changes --no-restore` also passes.

## Published release checkpoint

Baize **0.3.0-preview.7** is the published chat-first transport milestone.
The existing manual **Publish to NuGet** workflow uses `main` and the version in
`Directory.Build.props`, without version inputs. Tag-triggered publication remains
available. Publication now includes all 12 packages, including Runway and Fal;
normal package pushes disable implicit symbols, then push symbols separately.
CI retains the packed packages as the `nuget-packages` artifact.
This preview does not claim GM-3 or full B1-B12 completion.

Publication succeeded in [run 37252562357](https://github.com/jenolaszlo-sketch/penghou-baize/actions/runs/37252562357).
[CI run 37251898339](https://github.com/jenolaszlo-sketch/penghou-baize/actions/runs/37251898339)
passed all regression, coverage and six OS/framework adoption jobs.
All 12 packages and their symbols were pushed successfully and all package
versions are now indexed. A fresh consumer referenced all 12 exact published
Baize packages plus both exact neutral packages, restored solely from public
NuGet, verified no Hufu dependency, and ran core denial-before-dispatch / HTTP
body ownership checks on net8.0 and net10.0. No provider credentials or paid
calls were used. This completes delivery verification for this chat-first
release, not GM-3/4/5 or provider-by-provider batch/generation governance.

The next coding milestone is GM-3: batch and generation semantic authorization,
per-item context, handle binding, upload and asset retrieval. Hufu integration
remains a separate follow-up through the neutral abstractions.
