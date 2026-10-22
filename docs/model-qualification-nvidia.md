# NVIDIA hosted API source review and initial coverage

Status: Design research, checked 2026-10-22. No account, key, discovery endpoint
or inference request was accessed. Published documentation establishes candidate
contracts; it cannot establish this user's entitlement or live availability.

## Official sources and confirmed boundaries

| Subject | Finding and primary source | Design consequence |
| --- | --- | --- |
| Hosted chat | The hosted [LLM API reference](https://docs.api.nvidia.com/nim/reference/llm-apis) specifies `https://integrate.api.nvidia.com` with `POST /v1/chat/completions`. The [Llama 3.1 8B endpoint](https://docs.api.nvidia.com/nim/reference/meta-llama-3_1-8b-infer) describes OpenAI compatibility, model/messages, sampling/output limits and streaming. | Start with Baize OpenAi/Standard and base URL ending `/v1`; compatibility remains model/request specific. |
| Authentication | The [API Catalog quickstart](https://docs.api.nvidia.com/nim/docs/api-quickstart) describes Developer Program key creation. NVIDIA's [hosted endpoint example](https://docs.nvidia.com/nemo/microservices/26.3.1/troubleshooting/guardrails.html) sends `Authorization: Bearer` with `NVIDIA_API_KEY`. | Store only the secret reference. Do not log the header, key or copy credentials into inventory. |
| Discovery | The catalog and per-model API pages are the documented browsing mechanism. NVIDIA's [hosted network policy reference](https://docs.nvidia.com/nemoclaw/user-guide/openclaw/reference/network-policies) explicitly allows hosted `GET /v1/models` and chat completions. | An optional bounded discovery command can snapshot returned IDs. Treat its response as candidates, not capability, API-style or entitlement proof; curated inventory remains authoritative. |
| Per-model behavior | The [FAQ](https://docs.api.nvidia.com/nim/docs/product) explicitly notes model parameter differences. [Nemotron Nano's hosted request reference](https://docs.api.nvidia.com/nim/reference/nvidia-nemotron-3-nano-30b-a3b-infer) has model-specific output/sampling bounds. [Qwen's multimodal request reference](https://docs.api.nvidia.com/nim/reference/qwen-qwen3-5-122b-a10b-infer) describes image/video parts and optional chat-template kwargs. | Parameter and media manifests must be per-model. A Baize adapter unable to express a documented control yields a coverage gap, not a model failure. |
| Development/testing | The FAQ describes prototyping/development access. The [API Trial Terms](https://assets.ngc.nvidia.com/products/api-catalog/legal/NVIDIA%20API%20Trial%20Terms%20of%20Service.pdf), sections 1.1–1.4, limit trial access and distinguish internal testing/evaluation from production and subscriptions. | Compatibility qualification fits internal evaluation in purpose; the account owner must confirm applicable service/model terms and entitlement before execution. Do not infer permission for unrestricted published benchmarking or production use. |
| Quotas and cost | NVIDIA's [RAG troubleshooting](https://docs.nvidia.com/rag/2.5.0/troubleshooting.html) documents 429s from hosted rate limits; Trial Terms allow use/time/credit limits. No universal account-specific RPM, TPM, remaining credits or price was established by these sources. | Never encode a purported universal “40 RPM” or unlimited-free assumption. Resolve owner-approved limits and economics for the exact service; otherwise block live preflight. |
| Hosted versus downloaded NIM | [Self-hosted NIM API reference](https://docs.nvidia.com/nim/large-language-models/latest/api-reference.html) describes container endpoints including completion, Responses and management APIs. The hosted LLM reference separately points to downloadable NIM details. | Those container endpoints are not proof that hosted models support the same operations. Do not infer `/v1/completions`, Responses, health or metadata support for the hosted catalog. |

The prose extracted from these sources is a dated design review, not a stored
provider contract snapshot. A future approved run stores source URLs, access
date and permitted document hashes/excerpts with its inventory. Some catalog
`reference-link` links redirected to the documentation home during inspection;
direct per-model `/nim/reference/...` pages were readable. Missing/broken links
are documentation uncertainty, not a live model result.

## Representative initial selection

These are candidate identifiers, not verified Baize support or current account
availability. Choose three text representatives first and add vision only after
the model-specific content path and budget are reviewed.

| Candidate | Reason to include | Initial profile |
| --- | --- | --- |
| `meta/llama-3.1-8b-instruct` | Small general instruction model with a direct hosted reference | Baseline/streaming, then single tool only if advertised and representable |
| `nvidia/nemotron-3-nano-30b-a3b` | NVIDIA family and reasoning-sensitive output budget | Baseline/streaming using reviewed output cap; reasoning controls only if representable by the existing adapter |
| `deepseek-ai/deepseek-v4-flash` | Same model family as retained native-DeepSeek evidence, different serving provider | Baseline and separately selected tools/schema; start Standard dialect, never inherit DeepSeek native quirks by name |
| `qwen/qwen3.5-122b-a10b` | Optional multimodal representative | Baseline plus inline image only after confirming model ID and supported part shape in approved discovery/profile |

The first three identifiers appear in the hosted LLM catalog; the fourth has a
dedicated hosted request reference linked above. Discovery discrepancies produce
an inventory review task, not automatic substitution with a similarly named model.
Record observed response model/revision separately from the requested identifier.
If no revision is exposed, retain null and treat the identifier as a mutable alias.

Begin with two-phase selection: cheap baseline on the reviewed cohort, then
explicit deep profiles for capabilities that passed preflight. Larger coverage
uses a rotating deterministic subset grouped by serving provider, model family,
API dialect and capability, plus mandatory reruns for changed adapters or known
quirks. Keep a ledger of last-selected dates and exclusions. Daily runs must not
expand because discovery returns new models. Expand the inventory by reviewed
diff, freeze the next plan, and preserve a coverage denominator including models
never tested. Avoid multiplying every model by every mode, media fixture and
reasoning setting on every run.

Native Gemini, Anthropic and DeepSeek use separate inventory entries with their
existing Baize adapters. A provider-service identity identifies NVIDIA hosting
even when the adapter key is OpenAi and the model publisher is DeepSeek or Meta.
