# Orleans 10.3.1-alpha.1 journal fixtures

Captured from `origin/main` at fcca835c3141ac32874c70f687583d2c49ae4847 before migrating the implementation, using Microsoft.Orleans.Journaling 10.3.1-alpha.1 and the real Orleans journal manager with the recording storage boundary. The capture tests passed on that baseline. These fixtures are immutable compatibility inputs, not snapshots to regenerate on 10.4.

The append fixture records an RPC receiver token, two provider-qualified stream checkpoints with outbox-identity receipts, two outgoing messages and acknowledgement of the first. A manual clock starts at Unix epoch + 20000 days and advances one hour between writes. The snapshot fixture contains the resulting compacted tracker and outbox state, including the remaining outgoing message and both receipts, rather than the append operations. Neither fixture contains a retention cutoff or a receipt eviction operation. The adjacent expected JSON records the immutable public values from the same old-version session, including generated revisions and envelope identities.

The capture uses stable component names `tracker` and `outbox`, payload `OrderEvent`, and JSON journal format. `LegacyJournalTests` compares the complete recovered values and compacts/reloads them on the new preview. Business-state atomicity is covered separately by real DurableGrain and plain-Grain tests.
