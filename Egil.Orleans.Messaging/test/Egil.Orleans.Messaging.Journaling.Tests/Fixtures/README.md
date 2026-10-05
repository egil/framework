# Orleans 10.3.1-alpha.1 journal fixtures

Captured from `origin/main` at fcca835c3141ac32874c70f687583d2c49ae4847 before migrating the implementation, using Microsoft.Orleans.Journaling 10.3.1-alpha.1 and the real Orleans journal manager with the recording storage boundary. The capture tests passed on that baseline. These fixtures are immutable compatibility inputs, not snapshots to regenerate on 10.4.

Each append/snapshot fixture contains two outgoing messages followed by acknowledgement of the first, an RPC receiver token, provider-qualified stream checkpoints with outbox-identity receipts, recorded receiver timestamps and retention cutoffs, and a receipt eviction operation. A manual clock starts at Unix epoch + 20000 days and advances one hour between writes. Compaction writes the full state. The adjacent expected JSON records the immutable public values from the same old-version session, including generated revisions and envelope identities.

The capture uses stable component names `tracker` and `outbox`, payload `OrderEvent`, and JSON journal format. `LegacyJournalTests` compares the complete recovered values and compacts/reloads them on the new preview. Business-state atomicity is covered separately by real DurableGrain and plain-Grain tests.
