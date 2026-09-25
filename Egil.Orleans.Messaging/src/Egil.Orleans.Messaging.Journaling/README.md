# Egil.Orleans.Messaging.Journaling

Preview, opt-in journaled messaging components for .NET 10 and Orleans. This package
adds `IDurableMessageTracker` and `IDurableOutbox<T>` alongside Orleans' built-in
`IDurableValue<T>`, so receiver progress, business state, and outgoing messages can
share one journal write. Core `Egil.Orleans.Messaging` does not depend on Journaling.

## Compatibility

This preview targets **Microsoft.Orleans.Journaling 10.3.1-alpha.1** and Orleans
10.3.1. Its NuGet dependency specifies the matching minimum core OM version; use
the core version from the same release or a compatible later version. Journaling
is released alongside core OM with the same numeric version and a `-preview` suffix.
Use the exact tested Journaling version: newer previews can change lifecycle APIs.

The pinned Orleans package calls its internal lifecycle protocol `IJournaledState`
and its shared coordinator `IJournaledStateManager`. Newer Orleans source calls
these `IStateMachine` and `IDurableStateManager`; those APIs are not used here.
OM's `IStateManager<T>` is not needed for these components.

## Install and configure

```sh
dotnet add package Egil.Orleans.Messaging.Journaling --prerelease
```

Import `Egil.Orleans.Messaging.Journaling`, `Orleans.Journaling`, and
`Orleans.Journaling.Json`. Register the components once per silo:

```csharp
silo.UseJsonJournalFormat(options =>
    options.AddTypeInfoResolver(new DefaultJsonTypeInfoResolver()));
silo.AddMessagingJournaling();
silo.Services.AddSingleton<IJournalStorageProvider>(journalStorageProvider);
```

`DefaultJsonTypeInfoResolver` is in `System.Text.Json.Serialization.Metadata`.
Supply your own Orleans journal storage provider; this package does not select
or ship one. Its durability, atomic-write, and concurrency guarantees determine
the storage guarantees of the shared journal. The registration is idempotent and
supports arbitrary payload types and keyed component names. The JSON configuration
above also enables reflection metadata for your other durable business values.
The messaging codecs preserve host JSON converters and add their own reflection
fallback without modifying host serializer options.

## Compose a grain

Derive the grain from Orleans `DurableGrain` to enroll the shared manager in the
activation lifecycle. Inject components by name, using a **different, stable name
for every component in the grain**, including components of different types:

```csharp
public sealed class OrderGrain(
    [FromKeyedServices("business")] IDurableValue<OrderState> business,
    [FromKeyedServices("receiver")] IDurableMessageTracker tracker,
    [FromKeyedServices("outgoing")] IDurableOutbox<OrderEvent> outbox)
    : DurableGrain, IOrderGrain
{
    public async Task<bool> ReceiveAsync(OutboxSequenceToken token, OrderEvent message)
    {
        if (!tracker.TryAcceptMessage(token))
            return false;

        try
        {
            business.Value = ApplyMessage(business.Value, message);
            outbox.Add(message);
            await WriteStateAsync();
            return true;
        }
        catch
        {
            DeactivateOnIdle();
            throw;
        }
    }
}
```

`OrderState`, `OrderEvent`, `IOrderGrain`, and `ApplyMessage` are application types
and logic. Check the tracker first so duplicate messages skip business processing.
If processing fails after acceptance has been staged, this example deactivates and
recovers before accepting another command. One
`WriteStateAsync` gathers all registered components for that grain. Separately
written conventional grain storage does not participate in that commit. The
journal is shared pending state, not a transaction with arbitrary rollback:
interleaved calls can commit staged changes. On uncertain writes, choose an
explicit recovery policy before processing further commands. The executable
sample deactivates on write failure and recovers on the next activation.

## Work directly with the durable components

The interfaces mirror the immutable types' read and mutation operations. Mutators
change the registered component and return that same component for fluent use:

```csharp
outbox.Add(first).AddRange(rest);
tracker.EvictStreams(cutoff).Evict(sender, cutoff);
await WriteStateAsync();
```

Outbox reads include indexing, enumeration, `Count`, `IsEmpty`, `Envelopes`,
`Revision`, `Epoch`, and `LatestSequenceNumber`. `Remove` only removes a matching
head; `RemoveRange` removes matching IDs anywhere while preserving remaining order.
`Clear` preserves sequence history. Timestamped append overloads are available;
otherwise the registered `TimeProvider` supplies the time.

The tracker exposes all receive, lookup, and eviction overloads, including
provider-qualified stream cursors. Receive overloads with `out next` return this
same durable component; the simpler overloads without `out` avoid assignment.
`RegisterTimeProvider` affects subsequent receives, never replay timestamps.
Construction and restoration belong to Orleans; immutable `Create`/`Restore`
factories are not duplicated on the durable interfaces.

`AsImmutable()` returns the internally held `Outbox<T>` or `MessageTracker`, without
copying, including unsaved changes. Later mutations replace the internal reference,
so already captured values stay unchanged. Keep message payloads immutable too.
There are no public `Current` or `Committed` properties.

## Use the existing outbox processor

The processor still receives immutable `Outbox<T>` values:

```csharp
var processor = this.RegisterOutboxProcessor(() => outbox.AsImmutable(), options =>
    options.AcknowledgePostedAsync = async (items, cancellationToken) =>
    {
        outbox.RemoveRange(items);
        await WriteStateAsync(cancellationToken);
    });
```

The grain must implement `IOutboxGrain` and register the appropriate postmen as
usual. Saving and posting are independent application choices. This example
persists acknowledgements; an application can instead acknowledge in memory and
save later. The components do not enforce saving before posting. Configure
activation retry scheduling and transport delivery according to your application.

## Storage and format contract

Ordinary outbox writes contain a net delta: added envelopes, removed IDs, and the
resulting sequence high-water mark, epoch, and revision. Acknowledgements do not
rewrite queued payloads or business state. Messages added then removed before a
write contribute only their sequence metadata. Diff generation scans the original
and current envelope collections; this is a reduction in journal payload, not a
claim of constant-time mutations or measured storage-cost savings.

The tracker records individual receive/eviction operations, including original
receive times. Replay does not generate new IDs, revisions, timestamps, or receive
telemetry. Compaction writes full immutable snapshots. Each component uses an
Orleans JSON durable-value command containing an internal operation record:

| Component | Operation discriminators | Stored facts |
| --- | --- | --- |
| Outbox | `delta`, `snapshot` | Added envelopes, removed IDs, resulting metadata, or full snapshot |
| Tracker | `outbox`, `stream`, `snapshot`, `evict`, `evict-streams`, `evict-outboxes`, `evict-namespace`, `evict-sender` | Token/cursor and receive time, snapshot, or eviction scope and cutoff |

**JSON is the only supported journal format.** Other configured write formats fail
options validation. Native AOT, binary migration, importing existing grain blobs,
and cross-version journal migration are not supported in this preview. Component
names, payload types, and serializer configuration are part of your persisted
schema; keep them stable across restarts. Unknown operation kinds fail recovery.
Internal CLR types are not a public extension API, but their serialized shape is
persisted data. This initial preview does not promise compatibility with the old
non-packable prototype or future previews: upgrades need explicit release-note
review, replay testing against existing data, and a migration or fresh journal
when a format changes. Do not reinterpret an existing named component as another
type or rename it without migrating its data.

## Verification and development

From `Egil.Orleans.Messaging`, run:

```sh
dotnet test --solution Egil.Orleans.Messaging.slnx -c Release --report-xunit-trx
```

The release pipeline packs both packages and passes the matching core version with
the `-preview` suffix to the Journaling build. CI generates Journaling notes from
shared Messaging release tags and commits touching this project.
The dedicated tests exercise shared recovery, processor acknowledgements, immediate
posting without saving, compaction, net diffs, failures, and mutations during a
pending write.

The `egil-orleans-messaging` workflow builds, validates, and releases this package
alongside the core messaging packages.
The package is published with the matching core package from the messaging
release branch. Its NuGet version uses the core messaging version with a
`-preview` suffix. Configure NuGet trusted publishing for the
`egil-orleans-messaging` workflow before the first release.
