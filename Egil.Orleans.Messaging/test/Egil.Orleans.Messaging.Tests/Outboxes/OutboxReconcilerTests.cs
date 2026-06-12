namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxReconcilerTests
{
    private static readonly Exception DispatchError = new InvalidOperationException("dispatch failed");

    [Fact]
    public void Attempt_count_increments_while_item_stays_pending()
    {
        var reconciler = CreateReconciler();

        var first = reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);
        reconciler.PruneAttempts(["a"]);
        var second = reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, first.Failed[0].Attempt);
        Assert.Equal(2, second.Failed[0].Attempt);
    }

    [Fact]
    public void Prune_resets_attempt_count_for_item_removed_without_successful_post()
    {
        var reconciler = CreateReconciler();
        reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        // Grain dead-lettered "a" in ReconcileFailedAsync; it is no longer pending.
        reconciler.PruneAttempts([]);

        // An equal item enqueued later must start a fresh attempt sequence.
        var batch = reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, batch.Failed[0].Attempt);
    }

    [Fact]
    public void Prune_keeps_counts_for_items_still_pending_and_drops_the_rest()
    {
        var reconciler = CreateReconciler();
        reconciler.CreateBatch(
        [
            new OutboxDispatchResult<string>("kept", DispatchError),
            new OutboxDispatchResult<string>("dropped", DispatchError),
        ]);

        reconciler.PruneAttempts(["kept"]);

        var batch = reconciler.CreateBatch(
        [
            new OutboxDispatchResult<string>("kept", DispatchError),
            new OutboxDispatchResult<string>("dropped", DispatchError),
        ]);

        Assert.Equal(2, batch.Failed.Single(f => f.Item == "kept").Attempt);
        Assert.Equal(1, batch.Failed.Single(f => f.Item == "dropped").Attempt);
    }

    [Fact]
    public async Task Successful_post_removes_attempt_count()
    {
        var reconciler = CreateReconciler();
        reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        var posted = reconciler.CreateBatch([new OutboxDispatchResult<string>("a")]);
        await reconciler.ReconcileAsync(posted, CancellationToken.None);

        var batch = reconciler.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, batch.Failed[0].Attempt);
    }

    private static OutboxReconciler<string> CreateReconciler()
        => new(
            acknowledgePostedAsync: (_, _) => ValueTask.CompletedTask,
            reconcileFailedAsync: (_, _) => ValueTask.CompletedTask);
}
