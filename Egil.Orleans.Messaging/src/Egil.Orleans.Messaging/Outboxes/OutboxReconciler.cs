using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Outboxes;

internal sealed class OutboxReconciler<TOutbox>(
    Func<ImmutableArray<TOutbox>, CancellationToken, ValueTask> acknowledgePostedAsync,
    Func<ImmutableArray<(TOutbox Item, Exception Error, int Attempt)>, CancellationToken, ValueTask>? reconcileFailedAsync)
    where TOutbox : notnull
{
    private readonly Dictionary<TOutbox, int> attempts = [];

    public OutboxReconciliationBatch<TOutbox> CreateBatch(
        ImmutableArray<OutboxDispatchResult<TOutbox>> results)
    {
        var posted = ImmutableArray.CreateBuilder<TOutbox>();
        var failed = ImmutableArray.CreateBuilder<(TOutbox Item, Exception Error, int Attempt)>();
        foreach (var result in results)
        {
            if (result.Error is null)
            {
                posted.Add(result.Item);
            }
            else
            {
                failed.Add((result.Item, result.Error, IncrementAttempt(result.Item)));
            }
        }

        return new OutboxReconciliationBatch<TOutbox>(posted.ToImmutable(), failed.ToImmutable());
    }

    public async Task ReconcileAsync(
        OutboxReconciliationBatch<TOutbox> reconciliation,
        CancellationToken cancellationToken)
    {
        if (!reconciliation.HasWork)
        {
            return;
        }

        if (!reconciliation.Posted.IsDefaultOrEmpty)
        {
            await acknowledgePostedAsync(reconciliation.Posted, cancellationToken);
            foreach (var item in reconciliation.Posted)
            {
                attempts.Remove(item);
            }
        }

        if (!reconciliation.Failed.IsDefaultOrEmpty && reconcileFailedAsync is not null)
        {
            await reconcileFailedAsync(reconciliation.Failed, cancellationToken);
        }
    }

    /// <summary>
    /// Drops attempt counters for items that are no longer pending.
    /// </summary>
    /// <remarks>
    /// Attempt counts are keyed by item value equality and only removed by
    /// <see cref="ReconcileAsync"/> when the item posts successfully. Items
    /// that leave the outbox any other way — dead-lettered or dropped by
    /// <c>ReconcileFailedAsync</c>, or removed directly by the grain — would
    /// otherwise keep their entries for the lifetime of the activation, a
    /// slow memory leak that the owning grain cannot observe or clean up.
    /// The processor calls this with a fresh pending snapshot after each
    /// reconciliation.
    /// </remarks>
    public void PruneAttempts(ImmutableArray<TOutbox> pending)
    {
        if (attempts.Count == 0)
        {
            return;
        }

        if (pending.IsDefaultOrEmpty)
        {
            attempts.Clear();
            return;
        }

        var stillPending = new HashSet<TOutbox>(pending);
        var stale = attempts.Keys.Where(item => !stillPending.Contains(item)).ToList();
        foreach (var item in stale)
        {
            attempts.Remove(item);
        }
    }

    private int IncrementAttempt(TOutbox item)
    {
        attempts.TryGetValue(item, out var current);
        var next = current + 1;
        attempts[item] = next;
        return next;
    }
}