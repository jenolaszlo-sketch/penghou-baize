# Roadmap: streaming integrity and protocol reliability

Status: implemented. This document is retained as the design and regression
contract for Baize streaming providers.

## Goal

Harden Penghou.Baize against silent corruption introduced by provider
gateways, local inference servers, compatibility layers, and streaming
parsers.

Baize must guarantee that data received from a provider is either emitted to
the caller, deliberately consumed as protocol or tool metadata, or surfaced as
an explicit error. Buffered content must never disappear silently.

## Implemented state

Baize now provides the shared integrity behavior described here:

- every `LlmClientBase` provider passes canonical events through the shared
  `LlmStreamAssembler`, which audits normalized, emitted, consumed, and
  buffered UTF-16 code units and independently validates tool-call state at
  completion;
- `LlmClientBase.ReadSseEventsAsync` preserves and flushes a final SSE event at
  EOF, removes only the single optional ASCII space defined by SSE framing,
  preserves all other payload whitespace, and counts decoded SSE payloads at
  the provider boundary;
- Ollama counts decoded NDJSON records at the equivalent provider boundary;
- OpenAI, Gemini, and Ollama adapters reject streams that end without their
  expected terminal indication;
- `LlmStreamingExtensions.CollectAsync` assembles canonical text, reasoning,
  ordered parts, and native tool-call fragments, and rejects a tool-call
  buffer whose required name never arrives instead of silently dropping it;
- Claude releases incomplete buffered synthetic structured output before
  reporting a terminal protocol error or truncated-stream failure;
- `StreamCompleted` activity events expose privacy-safe provider, normalized,
  emitted, consumed, and buffered counts, finish reason, tool-call count, and
  protocol-warning count; warning codes are separate content-free activity
  events;
- router failover explicitly records canonical characters deliberately
  suppressed from an uncommitted failed attempt;
- `LlmStreamParityComparer` explicitly runs deterministic native and streaming
  paths, reports the first exact UTF-16 divergence, and does not retain response
  content in its result;
- deterministic provider fixtures for OpenAI, Claude, Gemini, and Ollama
  preserve a leading newline and an independently streamed final 20-character
  tail exactly.

Provider-character counts refer to decoded SSE payload characters or decoded
Ollama NDJSON record characters. Normalized, emitted, consumed, and buffered
counts refer only to canonical user-visible text, reasoning, and tool-argument
fragments. All character counts use UTF-16 code units. Provider and canonical
counts intentionally use distinct boundaries and are not asserted equal.

The completed source audit found no `buffer.Trim() == emitted` comparison or
equivalent transformed-buffer ownership bug. The shared marker lookahead keeps
the original buffer authoritative and releases every unrecognized prefix at
EOF. Relevant risks still exist:

- several live integration assertions call `Trim()` for semantic provider
  checks, but exact deterministic decoder, assembler, and parity fixtures now
  provide the authoritative character-preservation coverage;
- URL normalization and finish-reason classification use trimming outside raw
  stream-content ownership and do not mutate emitted content.

Future streaming work must repeat this audit for comparisons structurally
equivalent to `buffer.Trim() == emitted`, including normalization, prefix or
suffix matching, and marker detection performed against a transformed copy
before later mutating or releasing the raw buffer.

## Scope

Implement a shared stream-integrity layer used by all streaming providers.
Existing non-streaming APIs must remain unaffected.

### 1. Canonical stream assembly

Introduce a provider-independent stream assembler responsible for:

- consuming normalized provider deltas;
- assembling text and tool-call fragments;
- handling partial protocol or tool markers split across chunks;
- flushing all remaining buffered content on stream completion;
- preserving whitespace exactly unless a provider protocol explicitly defines
  otherwise.

Provider implementations should normalize transport-specific events and
delegate assembly to this shared component.

### 2. Terminal invariants

On EOF, `[DONE]`, `finish_reason`, or an equivalent terminal event:

- every buffered fragment must be emitted or explicitly consumed;
- no unexplained content may remain buffered;
- terminal flushing must not depend on trimmed or normalized text comparisons;
- unresolved buffered content must produce a diagnostic warning or protocol
  error instead of being discarded.

A clean upstream finish reason is not proof of Baize-side integrity.
`end_turn`, `stop`, `[DONE]`, and their equivalents mean only that the upstream
protocol terminated normally. Baize must still flush and validate every buffer
it owns before reporting successful completion.

The core conservation invariant is:

```text
received content =
    emitted content +
    explicitly consumed protocol content +
    remaining reported buffer
```

At successful completion:

```text
remaining reported buffer == 0
```

The stronger terminal rule is:

```text
if buffered content is not positively identified
as protocol or tool metadata,
it MUST be emitted verbatim
```

Character accounting must define whether counts use UTF-16 code units, Unicode
scalar values, or transport bytes. Diagnostics should expose distinct units
where comparing transport bytes with canonical characters is useful; unlike
units must not be presented as directly conserved values.

### 3. Streaming diagnostics

Add optional diagnostics containing at least:

```text
ProviderChunkCount
ProviderCharacterCount
NormalizedCharacterCount
EmittedCharacterCount
BufferedCharacterCount
FinishReason
ToolCallCount
ProtocolWarnings
```

Expose them through Baize's existing diagnostics and telemetry mechanisms,
without changing ordinary response content or requiring callers to consume new
result shapes for normal operation.

The counts should make divergence attributable to a specific boundary:

```text
Provider transport
    -> Provider decoder
    -> Normalized Baize delta
    -> Stream assembler
    -> Tool-call interpretation
    -> Consumer
```

### 4. Tool-call boundary safety

Tool-call detection and assembly must handle markers split at every possible
stream boundary. The original received buffer remains authoritative. Trimming
is forbidden as a mutation used for marker detection; a temporary comparison
value may be used only when the protocol explicitly permits it.

Every lookahead buffer used for marker detection must have one documented
owner and explicit EOF semantics. When no complete marker is positively
recognized, the owner must release every lookahead byte or character as normal
content, verbatim and in order. A partial marker prefix at EOF is user-visible
text, not metadata.

Tests must cover:

- a marker split between every pair of adjacent characters;
- markers immediately before EOF;
- leading newlines and whitespace;
- trailing whitespace;
- Unicode and multibyte text;
- an empty final chunk;
- text followed by a tool call;
- a tool call followed by text where supported;
- multiple tool calls;
- an incomplete marker at EOF;
- content that resembles a marker but is not one.

For a longest supported marker of `N` characters, partition tests must include
responses ending at `N - 1`, `N`, `N + 1`, and longer lengths, plus responses
shorter than the lookahead buffer. False-positive cases must end with every
possible proper prefix of a marker and verify that the prefix is emitted as
ordinary text at EOF.

Native provider tool-call deltas and content-encoded or compatibility-layer
tool calls must obey the same conservation and terminal rules, even if their
framing strategies differ.

### 5. Provider-boundary logging

When diagnostics are enabled, expose lightweight counts at each stage without
logging full prompts or output content by default. A completion event may
resemble:

```csharp
StreamCompleted
{
    ProviderChunkCount = 127,
    ProviderCharacterCount = 8431,
    EmittedCharacterCount = 8431,
    BufferedCharacterCount = 0,
    FinishReason = Stop
}
```

Instrumentation must be privacy-safe and inexpensive when disabled. Raw
content remains available only through Baize's separately enabled, bounded HTTP
diagnostic capture.

### 6. Streaming and non-streaming parity

For every adapter that supports both modes, deterministic protocol fixtures
must run the same request through streaming and non-streaming paths and assert
that the reconstructed response text is exactly equal. Equality means exact
character preservation, not semantic equivalence: tests must not trim,
normalize newlines, normalize Unicode, or ignore case.

Leading-whitespace regression cases must include responses beginning with:

```text
\n
\r\n
one or more spaces
\t
multiple newlines
```

They must also cover trailing whitespace. `Trim`, `TrimStart`, `TrimEnd`, or
equivalent normalization must not be used when comparing raw received,
buffered, emitted, streamed, or non-streamed content unless both operands are
deliberately normalized for a separately named protocol-level assertion. The
authoritative preservation assertion must always compare exact strings.

Where practical, provide an adapter debug or test helper that executes the same
deterministic request through both modes and reports the index and surrounding
counts of the first divergence. This helper is diagnostic tooling, not a
runtime recovery mechanism, and must not cause duplicate live billable calls by
default.

## Failure behavior

Baize must detect inconsistencies, not guess. It must not reconstruct missing
characters heuristically after an upstream server or compatibility layer has
already discarded them.

If Baize receives incomplete or internally inconsistent data, it must:

1. preserve everything actually received;
2. emit everything that can safely be emitted;
3. report the integrity or protocol problem;
4. never silently discard buffered user-visible content.

The design discussion must define how safely emitted partial content is
observable when an `IAsyncEnumerable<LlmStreamEvent>` subsequently throws, and
how collectors and routers preserve that diagnostic context without accepting
the partial response as successful.

## Architecture

Keep responsibilities separated:

```text
Provider adapter
    -> normalize transport
Baize stream assembler
    -> guarantee integrity
Tool and structured-output handling
    -> interpret canonical content
Consumer
```

Nuwa remains responsible for malformed structured output and JSON repair.
Baize is responsible for transport, protocol normalization, streaming
integrity, and tool-call framing. Baize must not silently repair transport
loss under the guise of structured-output repair.

## Completed implementation

1. [x] Define normalized delta, terminal signal, accounting units, warning, and
   assembler-result contracts without changing normal API results.
2. [x] Characterize every provider's current chunk, terminal, whitespace, text,
   reasoning, and tool-call behavior with protocol-level tests.
3. [x] Implement the shared assembler and exhaustive boundary-partition tests.
4. [x] Integrate one provider through the shared base path and compare canonical output and diagnostics
   against its characterization suite.
5. [x] Integrate all remaining streaming providers and audit the router/collector path.
6. [x] Add opt-in boundary telemetry and privacy tests.
7. [x] Add fixed-length missing-tail regression fixtures at transport, decoder,
   normalized-delta, and assembler boundaries.
8. [x] Add exact streamed/non-streamed parity fixtures and a privacy-safe
   divergence reporter. Baize's built-in chat adapters currently expose only
   streaming transport; future adapters implementing `ILlmCompletionClient`
   can use the same comparer directly.

## Required missing-tail acceptance scenario

Add a provider-neutral regression test modeling the failure mechanism seen with
an MLX-compatible gateway rather than depending on that specific server:

```text
Given:
  response starts with "\n"
  stream parser retains a 20-character lookahead
  no tool-call marker occurs
  provider terminates with clean end_turn

Then:
  complete streamed output equals complete non-streamed output exactly
  final 20 characters are emitted
  leading newline is preserved
  buffered character count is zero
```

Run the same contract against OpenAI-, Anthropic-, Ollama-, and other
compatible adapters wherever their protocol supports the fixture. The test is
successful only when it exercises the lookahead and terminal-flush mechanism;
an adapter that bypasses buffering needs an equivalent lower-level assembler
fixture rather than a vacuous pass.

## Acceptance criteria

- No buffered text can disappear during successful stream completion.
- All streaming providers use the common integrity rules.
- Tool markers work regardless of chunk boundaries.
- Leading and trailing whitespace are preserved exactly.
- Diagnostics identify character-count divergence between stages.
- Successful completion reports a zero remaining buffer.
- Incomplete or inconsistent input produces an explicit warning or protocol
  error and preserves everything actually received.
- Existing non-streaming APIs remain unaffected.
- Streaming behavior remains backward compatible for valid streams.
- Regression tests reproduce the fixed-length missing-tail class of bug.
- Deterministic streamed and non-streamed responses are exactly equal for every
  adapter supporting both modes.
- Leading newlines, CRLF, spaces, tabs, multiple newlines, and trailing
  whitespace survive unchanged.
- An unrecognized marker prefix and all other unclaimed lookahead content are
  emitted verbatim at EOF.
- Clean upstream termination is accepted only after Baize's buffers have been
  flushed and validated independently.
- Exact marker-length and lookahead-size boundary tests pass at `N - 1`, `N`,
  `N + 1`, longer, and shorter-than-lookahead response lengths.
