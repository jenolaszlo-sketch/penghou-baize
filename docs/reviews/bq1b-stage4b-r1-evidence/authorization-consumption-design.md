# F-01 authorization consumption design

## Identity and authenticated scope

R1 authorization schema 2.0 includes a cryptographically random GUID authorization ID and a provisioned authority ID. The ID, authority, proposal digest, plan hash, selected cases, output, endpoint/model, execution profile, transport identity, token/cost budget, credential reference, issue time, and expiry are part of the signed content. ECDSA P-256/SHA-256 with fixed-width P1363 signatures is verified using the configured public key. Re-encoding the same logical signed document produces the same authorization digest. Schema 1.0 reusable authorizations are rejected by `run-live`.

The runner contains no signer or token-issuance command. It reads the verification key from `BAIZE_LIVE_AUTHORIZATION_PUBLIC_KEY`; an owner-controlled external signing process would be required to create a live authorization. Tests generate ephemeral keys in memory only.

## Provisioning and authority selection

An operator explicitly provisions a registry with `authorization-registry provision --path <absolute-path>`. Provisioning creates the SQLite database and a random authority GUID. The authority ID is embedded in signed authorizations. At execution, the existing database path is selected by `BAIZE_AUTHORIZATION_REGISTRY_PATH`; `run-live` opens it in read/write mode and refuses to create it. A missing path, wrong authority, corrupt database, read-only database, symlink/reparse point, schema mismatch, failed pragma check, or I/O/SQLite exception is a fail-closed error.

All processes in one qualification authority must use the same registry path and authority. Operators must restrict the registry directory to the runner identity. On Unix the provisioner sets the main database file to user read/write only. On Windows the application does not rewrite ACLs; the operator must grant access only to the authorized runner/operator identities. The directory must also protect SQLite WAL/shared-memory sidecars. Network filesystems and multi-host access are not qualified.

## Atomicity and persistent record

SQLite is configured for WAL, `synchronous=FULL`, foreign keys enabled, and a 10-second busy timeout; each setting is read back and checked. A claim uses a non-deferred write transaction, inserts one row and commits before returning to the caller. The primary key on authorization ID, unique authorization reference, and unique execution correlation ID are transactionally enforced. SQLite's write serialization plus the unique constraints ensures competing processes cannot both insert a claim.

The row records authorization ID/reference, authorization digest, proposal digest, plan hash, execution profile identity, UTC claim time, execution correlation ID, claim status, and optional terminal result. It contains no credential value or request header. Terminal status is diagnostic; rows are never deleted or reset by normal execution.

## Dispatch order and failures

`run-live` validates the signed authorization, exact plan/proposal scope, build identity and deployment closure, live eligibility, transport profile, and budget before reading the configured credential. It rejects missing/blank/unavailable credentials before opening or creating run output. It then opens the pre-provisioned registry and commits the unique claim. Only after that commit does it recheck authorization expiry, deployment closure, and credential readiness. The same checks run at the HTTP admission boundary immediately before the request can be sent.

Any claim error, including a lost/ambiguous commit acknowledgement, prevents dispatch. If the claim did commit before an acknowledgement was lost, the claim remains and replay is denied. A later expiry, closure change, credential failure, cancellation, timeout, provider failure, or crash never releases the claim. A crash after commit and before dispatch can therefore consume an authorization without a provider request; this favors safety over availability.

Live `resume` refuses another dispatch whether the authorization is absent or already consumed. A new dispatch requires a new owner-approved authorization identity and signature.

## Tested and unproven guarantees

The offline qualification suite uses two barrier-synchronized runner subprocesses and observes exactly one dispatch receipt; it also tests process exit/restart, crash after claim before dispatch, duplicate/re-encoded authorization, ambiguous acknowledgement, and missing/read-only/corrupt stores. These are process-level and controlled SQLite tests on the Windows candidate host. They do not prove survival of power loss, storage-controller failure, malicious database replacement, or rollback to an older snapshot.

Restoring an older database snapshot or cloning the authority environment can erase claim history and is not detected by this design. Additional anti-rollback protection would need an external monotonic authority or trusted remote ledger. The host clock is trusted for expiry checks; clock rollback is not prevented. SQLite WAL behavior on network filesystems and multi-host deployments has not been qualified. The execution runtime, operating system, and hash-to-use race are outside the file closure and remain separate trust assumptions.
