# Resource boundaries and deferred extraction roadmap

Recorded 2026-10-02; documentation only. Direction:
[resource-abstractions architecture](../../Penghou/docs/resource-abstractions-architecture.md).

The immediate RA correction belongs to IO, Luban and Hufu. It deliberately excludes
Baize reorganization, a universal router and HTTP transport redesign.

- [ ] Later: extract existing router/model-lookup interfaces and their complete
  supporting type closure into a product-neutral capability contract package
  where reusable, with compatibility handling. Provider-specific contracts stay
  in Baize. Core client-contract extraction is a separate, broader decision.
- [ ] Later: separate diagnostic capture policy from a bounded storage sink.
  An IO adapter requires qualified create/append/delete or chunk-write semantics,
  redaction, retention and host-selected authority; today's Local patcher is not
  a replacement for streaming diagnostic files.
- [ ] **VFS-5/6, deferred:** a host simulation must explicitly block, fixture or
  simulate model/tool/network effects. A filesystem overlay cannot make a real
  model call harmless, deterministic or free of external effects.

Baize owns provider protocol construction, parsing and safe retries. The new
[governable model/HTTP roadmap](governable-model-http-plan.md) owns replaceable
transport delivery independently of RA. Host-selected implementations govern
authority, credential custody and budget reservations; Baize never depends on
Hufu. Filesystem abstractions do not absorb general authenticated HTTP. Handoffs
cite GM gates for this refactor and RA/VFS gates for separate resource work.
