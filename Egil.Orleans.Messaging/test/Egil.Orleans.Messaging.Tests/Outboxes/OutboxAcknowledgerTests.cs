namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxAcknowledgerTests
{
    private static readonly Exception DispatchError = new InvalidOperationException("dispatch failed");

    [Fact]
    public void Attempt_count_increments_while_item_stays_pending()
    {
        var acknowledger = CreateAcknowledger();

        var first = acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);
        acknowledger.PruneAttempts(["a"]);
        var second = acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, first.Failed[0].Attempt);
        Assert.Equal(2, second.Failed[0].Attempt);
    }

    [Fact]
    public void Prune_resets_attempt_count_for_item_removed_without_successful_post()
    {
        var acknowledger = CreateAcknowledger();
        acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        // Grain dead-lettered "a" in AcknowledgeFailuresAsync; it is no longer pending.
        acknowledger.PruneAttempts([]);

        // An equal item enqueued later must start a fresh attempt sequence.
        var batch = acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, batch.Failed[0].Attempt);
    }

    [Fact]
    public void Prune_keeps_counts_for_items_still_pending_and_drops_the_rest()
    {
        var acknowledger = CreateAcknowledger();
        acknowledger.CreateBatch(
        [
            new OutboxDispatchResult<string>("kept", DispatchError),
            new OutboxDispatchResult<string>("dropped", DispatchError),
        ]);

        acknowledger.PruneAttempts(["kept"]);

        var batch = acknowledger.CreateBatch(
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
        var acknowledger = CreateAcknowledger();
        acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        var posted = acknowledger.CreateBatch([new OutboxDispatchResult<string>("a")]);
        await acknowledger.AcknowledgeAsync(posted, CancellationToken.None);

        var batch = acknowledger.CreateBatch([new OutboxDispatchResult<string>("a", DispatchError)]);

        Assert.Equal(1, batch.Failed[0].Attempt);
    }

    private static OutboxAcknowledger<string> CreateAcknowledger()
        => new(
            acknowledgePostedAsync: (_, _) => ValueTask.CompletedTask,
            acknowledgeFailuresAsync: (_, _) => ValueTask.CompletedTask);
}
