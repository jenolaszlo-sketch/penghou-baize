# BQ-1b Stage 0 boundary probes

Audited production source: `3986dde792f6af49f4f3a51e27db1a6d721d691f`.
See the [bounded feasibility audit](../../model-qualification-bq1b-stage0-audit.md)
and [execution decisions](../../model-qualification-bq1b-stage0-decisions.md).

This non-packable net10.0 console project is outside the solutions. It references
the existing provider/core/router/contracts projects; it changes no production
code, dependency version or API. Its assembly name uses the existing Contracts
friend identity to exercise the accepted internal baseline scenario. No xUnit
runtime is added. NuGet.Config clears sources; use an installed .NET 10 SDK and
locally cached existing dependencies. Command-local NuGetAudit=false avoids an
online audit during the offline restore and changes no permanent policy.

Every native client receives a factory ending in the custom `Terminal` handler;
there is no SocketsHttpHandler, listener or real provider connection in the probe
execution. All URLs, credentials and payloads are synthetic. The custom `probe`
provider delegates to the real OpenAI client solely to count deferred construction
or demonstrate duplicate factory installation; it is not a qualified custom
provider path. The audit separately cites pinned runtime source for behavior
below an actual network backend. No physical wire-count proof is claimed.

`Fixtures.cs` contains deliberately small, non-durable counters, single-slot
guards, an ambient lease and scripted handlers/streams. They demonstrate public
ports and counterexamples. They do not implement BQ-1b admission, its journal,
token/cost ledger, production concurrency supervisor or five-second cleanup.
In particular, production stop must revoke leases before awaiting disposal,
not depend on an enumerator finally block that might be stuck.

`Program.cs` runs 26 sequentially reported cases. Its invariant failure throws
and returns nonzero. Expected defective/limited behavior is asserted rather
than hidden. The cases cover:

- Four real native chat adapters, fake baseline streams, one semantic/neutral/
  backend entry and the serialized 512 output cap.
- Pre-credential denial and credential-free metadata resolution, versus runtime
  endpoint validation that constructs clients outside semantic admission.
- Fresh admission on re-enumeration, fallback despite MaximumAttempts=1 and
  double counting if the same guard is installed on outer and inner factories.
- Neutral/backend count divergence from handler retries; retry/destination
  denial at a final application guard; a barrier-released two-participant
  final-slot race.
- Request rejection before send, decoded-body read overshoot/disposal, separate
  read cancellation, existing total timeout and synchronously blocking disposal.
- Deferred construction after a cancelled waiter, reload drift and binding
  rejection, and revocation of a captured background scope.
- Partial Ollama usage with a derived total and unexposed OpenAI reasoning
  details. Neither is complete billable usage or zero unknown cost.

`results.jsonl` stores all observations. Every `physicalWireAttempts` is null
because there is no measured wire-attempt domain; the harness makes zero actual
network/provider calls. Safe output contains only fixture names, numeric/status
observations and normalized usage, not keys, raw HTTP payloads or exception text.
No observation is a live model qualification claim.

The manifest binds the audited base, SDK/runtime, source and results. SHA-256
values use UTF-8 without BOM with CRLF normalized to LF, including final newlines.
This handles normal Git checkout line endings. They are audit artifact hashes,
not a BQ-1b run schema or durable journal implementation.

Reproduction from the repository root:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet restore docs/reviews/bq1b-stage0-probes/BoundaryProbes.csproj --configfile docs/reviews/bq1b-stage0-probes/NuGet.Config -p:NuGetAudit=false -m:1 --verbosity quiet
dotnet build docs/reviews/bq1b-stage0-probes/BoundaryProbes.csproj --no-restore --disable-build-servers -m:1 --verbosity quiet
./docs/reviews/bq1b-stage0-probes/Verify-Probes.ps1
```

The verifier runs the already-built executable, checks all 26 invariants and
compares its full stdout with the saved results case/order-sensitively. It also
checks source/result hashes. stderr only prints a completion count. Do not
overwrite saved results when inspecting another build; use a new evidence cohort.
