# Async policy

Baize libraries use asynchronous APIs end to end. Library-owned awaits that
continue with provider, routing, polling, or repair work use
`ConfigureAwait(false)`. Async iterators and thin adapter pass-throughs may
omit it because iterator scheduling is controlled by the consumer and no
synchronization-context affinity is assumed.

Synchronous compatibility APIs may bridge to asynchronous resolution only at
their public boundary. Dispose paths must never block on asynchronous file or
network work; diagnostics provide separate synchronous and asynchronous
completion paths.

This is a semantic policy rather than a blanket CA2007 rule: a solution-wide
rule would incorrectly flag async iterators and framework adapter methods.
