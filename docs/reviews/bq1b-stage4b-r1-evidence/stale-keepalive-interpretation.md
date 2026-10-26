# F-08 stale keep-alive probe interpretation

The test is named `TwoSeparatelyAdmittedInvocations_ReconnectAfterKeepAliveClose`.

The loopback server completes the first request and sends its response with keep-alive enabled, then closes that connection. The test separately invokes the harness a second time. The second invocation is admitted separately and opens a new connection. Assertions observe two admission-gate admissions, two complete server requests, and two TCP connections.

This proves that a later, separately admitted invocation reconnects after a keep-alive connection has closed. It does not simulate a stale pooled connection within one invocation, does not establish a transport retry, and does not prove replay prevention. The two requests are intentional separate invocations. Retry/replay prevention is covered by the single-attempt transport and authorization-consumption tests, not by this keep-alive probe.

The original test evidence remains in history; R1 corrects the name and interpretation without changing accepted historical reports.
