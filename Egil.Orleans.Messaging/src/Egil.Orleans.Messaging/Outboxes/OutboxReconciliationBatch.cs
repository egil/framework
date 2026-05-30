using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Outboxes;

internal readonly record struct OutboxReconciliationBatch<TOutbox>(
    ImmutableArray<TOutbox> Posted,
    ImmutableArray<(TOutbox Item, Exception Error, int Attempt)> Failed)
    where TOutbox : notnull
{
    public bool HasWork => !Posted.IsDefaultOrEmpty || !Failed.IsDefaultOrEmpty;
}