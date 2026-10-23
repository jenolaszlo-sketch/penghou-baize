# BQ-1a implementation and independent review handoff

Date: 2026-10-23. Status: implementation complete, ready for independent Astra
review. This is deterministic extraction evidence, not live model qualification
or approval to merge. BQ-1b, BQ-2 and graduation remain outside this change.

## A. Git binding

- Repository: `jenolaszlo-sketch/penghou-baize`.
- Branch: `implementation/baize-bq1a`.
- Exact design base: `355685a04931ec0d87ae3b74232b123e9d04d86e`
  (`design/baize-model-qualification`).
- Implementation commit: `7b7b3b57365b1075b46cdffd65f0e48dd4350b02`.
- This report is a documentation-only follow-up to that implementation commit.
  Review the implementation commit against the design base; review the branch
  tip for the complete handoff. The delivery message records the final tip.
- Implementation worktree: `C:/Users/Laszlos/source/repos/Solo/baize-bq1a`.
- The original checkout remains on main at
  `b8f86893851726eb3b40a556435f9202b2460673`; its pre-existing modification to
  `docs/roadmap-experience-signals.md` remains outside this branch and untouched.
  The dedicated worktree began clean at the committed design base.

The implementation changes 15 files: the two solution files; the offline CI
workflow; `LiveClientFactory.cs`, `LiveProviderTests.cs` and the integration
project reference; five files in Qualification.Contracts; four files in
Qualification.Tests. This report is the sixteenth changed file. No `src/` file
or production project/package reference changed.

## B. Extraction and implementation

The mapping was reported before implementation:

| Existing public xUnit entry point | Shared scenario | Contracts from the same invocation |
| --- | --- | --- |
| `StreamingSmokeTest_ReturnsExpectedContentAndDiagnostics` | `RunBaselineAsync` | `chat.baseline@2`, `chat.streaming-integrity@1` |
| `NativeToolSmokeTest_ReturnsSchemaValidArguments` | `RunSingleToolAsync` | `tools.single@2`, `chat.streaming-integrity@1` |

`tests/Penghou.Baize.Qualification.Contracts/` is a non-packable .NET 10
project, with no xUnit dependency. Its only project reference is Baize Tools,
which supplies the existing schema validator and transitively the core types.
All added contract types are internal; friend access is limited to the two
test consumers. No existing public Baize API changes.

| File | Responsibility |
| --- | --- |
| `QualificationContracts.cs` | Versioned descriptors, assertion/result records, applicability and output-budget state, execution context, safe numeric diagnostics |
| `QualificationFixtures.cs` | Original synthetic prompts and canonical tool schema; fresh requests at temperature 0 and a 512-token output cap |
| `QualificationScenarios.cs` | One stream invocation, existing `CollectAsync`, independent raw-delta observations, semantic checks and existing `StructuralToolArgumentValidator` |
| `IntegrityTelemetry.cs` | Scoped observation of existing Baize `StreamCompleted` Activity events; typed counts only, isolated by invocation trace |
| `Penghou.Baize.Qualification.Contracts.csproj` | Non-packable framework/reference/friend-assembly configuration |

The offline test project contains `ScenarioFixture.cs` (controlled events and
telemetry), `QualificationScenarioTests.cs` (contract positive/negative cases),
`ProviderFixtureTests.cs` (real adapter/router over an in-memory HTTP handler),
and its project file. CI now runs that deterministic project.

### Contract and assertion versions

Assertion IDs are versioned by their enclosing contract, rather than by a
second independent version number.

| Contract | Fixture | Assertions |
| --- | --- | --- |
| `chat.baseline@2` | `baize-token@1` | `response.usable`, `response.expected-value`, `response.finish`, `execution.completed`, `diagnostics.router` |
| `chat.streaming-integrity@1` | `observed-stream@1`, observing either scenario | `stream.sequence`, `stream.terminal`, `stream.reconstruction`, `stream.tool-deltas`, `stream.completed`, `stream.accounting`, `stream.buffer`, `stream.warnings`, `stream.dispatches` |
| `tools.single@2` | `echo-value@1` | `tool.count`, `tool.name`, `tool.identity`, `tool.json`, `tool.schema`, `tool.value`, `tool.unrepaired`, `response.finish`, `execution.completed`, `diagnostics.router` |

Each descriptor declares maximum dispatches 1 and maximum output tokens 512.
Both scenarios invoke the supplied stream delegate once; the observer never
issues another request. Unsupported or insufficient budget states block before
invocation. Unknown support remains explicit and permits the fixed 512-token
probe; a length finish produces `output-budget-exhausted`, without increasing
the cap. Native tools require the execution context's effective capability.

The caller owns endpoint/model binding, credentials, admission and transport
retry configuration. Only the two extracted live wrappers request the new
test-factory `singleDispatch` option (`MaximumAttempts=1`). Other test factory
callers retain their previous behavior. Production routing policy is unchanged.

The contracts never load environment settings, select endpoints, write files,
install HTTP capture, retry, or invoke repair. Results retain stable codes,
counts, finish classification, usage numbers and status codes, rather than raw
content, tool arguments, provider identifiers or exception messages. Cancellation
before and during invocation is explicit; stream enumerators and telemetry
listeners are disposed. Concurrent execution tests verify isolation and ambient
Activity restoration.

### Deliberately strengthened assertions

- Baseline changes substring containment to exact trimmed `BAIZE_OK` and
  requires successful completion. Raw reconstruction preserves all whitespace.
- Expected text cannot hide a missing/unsuccessful/duplicate finish, late error,
  post-terminal payload, incomplete provider diagnostics or observed failed
  completion/protocol warnings.
- Streaming checks exact canonical content/reasoning/tool argument preservation,
  terminal and delta consistency, normalized character accounting, zero leftover
  buffer, no warnings, and observed dispatch counts. Missing integrity telemetry
  is `NotObserved`/`Inconclusive`, not a successful observation.
- Tools check the original call ID and name, complete JSON, the full canonical
  schema (including required string value and no extra properties), the expected
  value and absence of repair. The collector's generated fallback ID cannot
  satisfy the independent original-delta identity assertion.
- Tool finish accepts `Stop` or `ToolCall` classifications to accommodate
  existing provider mappings. IDs must be nonblank, at most 256 characters, free
  of whitespace/control characters, and consistent across fragments.

## C. Deterministic test evidence

Environment: Windows, .NET SDK 10.0.401. Commands below ran from the implementation
worktree. Before aggregate and integration test runs, the process environment was:

```powershell
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path (Get-Location) '__bq1a_no_environment_file__'
```

The explicit nonexistent environment-file path prevents ancestor `.env.local`
loading. The fake-HTTP tests use `fixture.invalid` with a custom handler that
never opens a socket. No live/paid provider requests were executed.

| Exact command | Actual final outcome |
| --- | --- |
| `dotnet build Penghou.Baize.slnx --configuration Release --no-restore --verbosity quiet` | Exit 0; zero warnings/errors. An earlier full build with restore also passed. |
| `dotnet test tests/Penghou.Baize.Qualification.Tests/Penghou.Baize.Qualification.Tests.csproj --configuration Release --no-restore --logger 'trx;LogFileName=bq1a-contracts.trx'` | Exit 0; 47 passed, zero failed/skipped. |
| `dotnet test Penghou.Baize.slnx --configuration Release --no-build --verbosity quiet --logger trx` | Exit 0; 1,860 passed, zero failed, 28 skipped across 26 project/framework runs. |
| Integration opt-out command below | Exit 0; project built; exactly two tests found and both skipped under the live opt-out. |
| Integration discovery command below | Exit 0; lists exactly the two original entry points in the mapping above. |
| `./eng/verify-transport-dependencies.ps1` | Exit 0; all 12 production dependency closures verified. |
| `dotnet format Penghou.Baize.slnx --verify-no-changes --no-restore --verbosity quiet` | Exit 1; existing production whitespace issues detailed below. A new test whitespace issue found on the initial run was corrected and the changed files verified separately. |
| `dotnet format Penghou.Baize.IntegrationTests.slnx --verify-no-changes --no-restore --verbosity quiet` | Exit 1; the same existing production whitespace issues only; no wrapper formatting errors. |
| `git diff --check` and `git diff --cached --check` | Passed. |

The exact integration commands were:

```powershell
dotnet test Penghou.Baize.IntegrationTests.slnx --configuration Release --filter 'Capability=Baseline|Capability=Tools' --logger 'trx;LogFileName=bq1a-integration-optout.trx' --verbosity quiet
dotnet test tests/Penghou.Baize.IntegrationTests/Penghou.Baize.IntegrationTests.csproj --configuration Release --no-build --list-tests --filter 'Capability=Baseline|Capability=Tools' --verbosity quiet
```

The exact successful focused format verification was:

```powershell
$bqFiles = @(rg --files tests/Penghou.Baize.Qualification.Contracts tests/Penghou.Baize.Qualification.Tests -g '*.cs')
dotnet format Penghou.Baize.slnx --verify-no-changes --no-restore --include $bqFiles --verbosity quiet
```

An early aggregate run using `--no-restore` exited 1 because a newly added test
used an init property as a constructor argument. That test source was corrected;
the 47-case focused run rebuilt it successfully, and the subsequent aggregate
run above passed. Initial focused compile errors were also resolved before
these final results. An earlier broad command without explicit live opt-out was
rejected by automatic approval review before execution; the safe command above
was permitted and completed.

Aggregate results (counts are executions, including repeat framework coverage):

| Suite | Frameworks | Passed per framework | Skipped per framework |
| --- | --- | --- | --- |
| Core | net8.0, net10.0 | 256 | 0 |
| Claude | net8.0, net10.0 | 52 | 0 |
| OpenAI | net8.0, net10.0 | 83 | 7 |
| Ollama | net8.0, net10.0 | 13 | 0 |
| Gemini | net8.0, net10.0 | 91 | 7 |
| Runway | net8.0, net10.0 | 50 | 0 |
| Fal | net8.0, net10.0 | 58 | 0 |
| Router | net8.0, net10.0 | 96 | 0 |
| Batch | net8.0, net10.0 | 56 | 0 |
| Tools | net8.0, net9.0, net10.0 | 71 | 0 |
| Extensions.AI | net8.0, net10.0 | 22 | 0 |
| Diagnostics | net8.0, net10.0 | 23 | 0 |
| Qualification | net10.0 | 47 | 0 |

The 28 existing skips describe fixture capabilities: immediate providers do not
queue image submissions (24), or the fixture's multiple-candidate capability
does not match that test (4). They are not new qualification passes.

The 47 qualification cases cover correct text/finish, unsuccessful and length
finishes, missing/extra text, truncation and late errors, duplicate/missing
terminals, post-terminal payload, whitespace conservation, accounting/buffer/
warning failures, absent/failed telemetry, multiple dispatch observations,
fragmented tools, wrong/missing names and IDs, conflicting/negative identities,
malformed JSON, required/type/extra-property/value failures, repair rejection,
budget/tool applicability, cancellation, privacy and concurrent isolation.
Real-adapter fixtures verify request cap serialization, normal tool mapping,
missing terminals, the ordering finding below, router diagnostics, zero HTTP
calls during endpoint validation, and exactly one HTTP call on a 503 response.

TRX artifacts remain in ignored local `tests/*/TestResults/` directories; raw
provider payloads and credentials are not committed. Coverage thresholds,
package-consumer qualification and Linux execution were not run in this stage.

## D. Compatibility assessment

All original public test method declarations, `[Fact]` attributes and capability
traits in `LiveProviderTests` compare identically against the design base.
`LiveTestSettings`, `LiveEnvironment`, `LiveTestTraits` and `LiveTelemetryScope`
are unchanged. Provider selection, live opt-in, tool opt-in and wrapper-owned
diagnostic setup remain in the integration layer. Endpoint validation remains
where the original baseline used it. Other capability tests are unchanged.

The shared assembly's restored dependency closure contains no xUnit, Test SDK,
Marang, Zhinu, Hufu or Hongxian. No production dependencies, provider SDKs,
transport boundaries, Gemini/DeepSeek workarounds or Nuwa repair behavior changed.
The ordinary solution now builds/runs the new offline tests; live integration
tests remain in the separate integration solution.

Compatibility evidence here establishes compilation, discovery, opt-out and
controlled adapter/router execution. It does not establish a live provider pass.

## E. Risks, findings and limitations

1. **Existing OpenAI adapter ordering finding:** when one wire chunk contains a
   tool delta and a finish reason, `OpenAiChatClient.StreamCoreAsync` emits the
   finish event before the tool delta. A valid separate-terminal fixture passes,
   while `CoTerminalToolChunk_ExposesExistingAdapterOrderingFinding` records the
   co-terminal case as `stream.sequence: invalid-event-sequence`. The stricter
   contract can therefore reject this existing adapter output even with complete
   arguments. Production code was not changed or silently accommodated. Astra
   should review the sequence policy and triage the adapter finding separately
   before relying on live results affected by this shape.
2. **Collector ID synthesis:** existing `CollectAsync` can generate an ID when
   none was emitted. Independent raw-delta validation deliberately rejects that
   as native identity evidence. Other provider adapters may synthesize IDs before
   their public stream; this layer cannot prove wire-level identity provenance.
3. **Telemetry availability:** buffer/accounting/warning assertions consume the
   existing public Activity event representation. A custom client without these
   events yields inconclusive integrity results. The scoped listener creates a
   fresh trace and restores the caller's ambient Activity; no global mutable
   execution settings or HTTP capture are installed.
4. **Dispatch enforcement boundary:** the scenarios guarantee one delegate
   invocation and reject observed extra attempts, but cannot prevent an arbitrary
   delegate from retrying before returning. The extracted wrappers disable router
   retries and the fake HTTP regression verifies it. Hard transport admission,
   accounting and resumability belong to BQ-1b. Unknown output-budget support is
   not inferred from provider error strings or treated as proved support.
5. **Future consumer access:** BQ-1b must add its assembly as an explicit friend
   and reference this shared project, then consume these same scenarios and
   assertions. No CLI, catalogue, journal or orchestration was introduced here.
6. **Pre-existing formatting failure:** full-solution format verification reports
   whitespace at lines 19, 44 and 52 in
   `src/Penghou.Baize.Tools/Schema/JsonSchemaGenerator.cs`. Its Git blob is
   `14d5fd54e053d0dabf9c7f38769d432d85a67b12` both at the design base and in this
   worktree. It is unchanged; fixing production files was outside this extraction.

## F. Independent review handoff

Review implementation commit `7b7b3b57365b1075b46cdffd65f0e48dd4350b02` against
base `355685a04931ec0d87ae3b74232b123e9d04d86e`, plus this report at branch tip.
Independent review has not yet occurred. In particular, inspect:

- Extraction equivalence and intentional strengthening; no lost trait coverage.
- Independent raw reconstruction/identity checks versus existing collector and
  validator reuse; no competing schema implementation.
- The co-terminal OpenAI ordering finding and potential rejection of otherwise
  valid provider chunks; do not silently weaken the contract to clear it.
- Distinct failed, blocked, cancelled and inconclusive outcomes and safe result
  content; availability/authentication failures are not model qualification wins.
- Scoped telemetry isolation, cancellation/disposal and caller-owned dispatch
  enforcement; no hidden credential, HTTP-capture or file-writing side effects.
- Reusability by the future runner, fixed budgets and BQ-1a scope boundaries.

## G. Disposition

BQ-1a implementation is complete and committed for independent review, with the
documented adapter finding and pre-existing formatting limitation. No model has
been newly qualified, no independent review approval is claimed, and no merge,
package publication, paid qualification or BQ-1b implementation is authorized
by these green deterministic tests.
