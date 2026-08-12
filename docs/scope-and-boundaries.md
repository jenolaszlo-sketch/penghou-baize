# Scope and boundaries

Penghou.Baize is a provider-neutral .NET client and routing layer for calling
language models reliably. It normalizes completion streaming, tool calls,
structured output, multimodal input, native batch submission, diagnostics, and
capability-aware routing while preserving provider-specific evidence and
extension points.

The simplest boundary is:

> Baize chooses and communicates with a model. The application decides what
> work should happen. An agent or workflow framework coordinates that work.

This page makes that boundary explicit. `Planned` describes an intended Baize
capability, not a committed release date. `Considering` means that the project
needs more provider evidence and contract design before making a commitment.

## What Baize is not

| Area | What Baize does today | What Baize does not do | Direction |
| --- | --- | --- | --- |
| Agent orchestration | Supplies model clients, routing, tool-call data, usage, and diagnostics that an agent framework can consume | Plan tasks, coordinate agents, run autonomous loops, or decide when work is complete | Out of scope; integrate Baize with the agent framework chosen by the application |
| Durable workflows | Exposes synchronous streaming and provider-native batch primitives | Persist workflow state, checkpoint application work, compensate operations, schedule jobs, or recover an application workflow after a crash | Out of scope; use a workflow engine or durable job system |
| Tool execution | Sends tool definitions and normalizes model tool calls and results | Execute arbitrary tools, authorize calls, sandbox code, approve side effects, or retry application operations | Out of scope; the host owns execution and policy |
| Real-time bidirectional sessions | Supports ordinary server-to-client completion streaming | Maintain WebSocket sessions for continuous audio, video, or text exchange, such as Gemini Live | Considering a separate live-client contract |
| Generated media | Accepts image, audio, video, and document inputs where a provider supports them | Return generated images, speech, video, music, or other binary artifacts through `ILlmClient` | GenerationClient planned; see the [roadmap](roadmap-generation-client.md) |
| Long-running model operations | Coordinates provider-native chat batches and can poll their results | Provide a general operation handle for background agents, resumable streams, progress, or generated-media jobs | Planned as part of GenerationClient where required; broader workflow behavior remains out of scope |
| Prompt management | Transports messages, system instructions, tools, and schemas | Version prompts, render application templates, run prompt experiments, or maintain a prompt registry | Out of scope |
| RAG and semantic memory | Allows custom routing memory and request metadata | Ingest documents, create embeddings, operate a vector store, retrieve context, or manage conversation memory | Out of scope; compose Baize with dedicated retrieval and storage components |
| Model evaluation | Captures usage, diagnostics, compatibility evidence, and integration-test results | Judge response quality, benchmark models, curate datasets, or automatically learn which model is best for a business task | Considering extension points only; evaluation systems remain separate |
| Secrets management | Resolves credentials through an application-supplied secret abstraction | Store, rotate, encrypt, or distribute credentials | Out of scope; integrate a platform secret provider |
| Application policy | Supports capability filters, named routes, fallbacks, and custom route providers | Define tenant authorization, data residency, content policy, spending approval, or retention policy | Out of scope; applications can enforce these through Baize extension points |
| Universal provider parity | Normalizes portable concepts and retains provider-specific diagnostics | Promise that every provider or every model supports identical tools, schemas, media, streaming, and lifecycle semantics | Intentionally not promised; compatibility is recorded per model and API style |
| Provider SDK replacement | Offers a stable portable surface for common model operations | Expose every provider feature immediately or conceal meaningful provider differences | Intentionally selective; provider-specific configuration and diagnostics remain available |

## Client boundaries

Different operation lifecycles should not be forced into one response type.
The intended client boundaries are:

| Client surface | Lifecycle | Status |
| --- | --- | --- |
| `ILlmClient` | Request/response completion with optional server-to-client streaming, tools, structured output, and multimodal input | Available |
| `IBaizeBatchClient` | Provider-native asynchronous batches of completion requests | Available |
| GenerationClient | Generated image, audio, video, or other artifacts; may expose progress and operation handles | Planned |
| Live client | Persistent bidirectional real-time sessions over transports such as WebSockets | Considering |
| Embedding client | Vector generation for text and multimodal content | No Baize contract planned currently; prefer established .NET abstractions |

An API style is separate from a client surface. For example, Gemini's
Interactions API can carry ordinary text completions and generated images, but
those results should still be exposed through the appropriate provider-neutral
contract rather than one overly broad response object.

## Composition is expected

Baize is designed to sit inside a larger application rather than own it. A
typical system may combine:

- Baize for provider clients, capability-aware routing, normalization, batch,
  diagnostics, and compatibility handling;
- an agent framework for planning and autonomous tool-use loops;
- a workflow engine for durable, long-running coordination;
- application services for tool execution and authorization;
- dedicated stores for secrets, documents, vectors, and conversation state;
- observability and evaluation systems for operational and quality feedback.

Keeping these responsibilities separate lets applications adopt Baize without
also adopting an opinionated agent runtime or orchestration architecture.
