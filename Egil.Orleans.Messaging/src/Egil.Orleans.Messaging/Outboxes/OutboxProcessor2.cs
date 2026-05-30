using System.Collections.Immutable;
using System.Threading.Channels;

namespace Egil.Orleans.Messaging.Outboxes;

internal class OutboxProcessor2Dispatcher<T> where T : notnull
{
    private readonly OutboxPostmanRegistry<T> register;
    private readonly IGrainTimer dispatchTimer;
    private OutboxSendOperation<T>? batch;

    public OutboxProcessor2Dispatcher(
        IGrainBase owner,
        bool interleaveDispatch,
        OutboxPostmanRegistry<T> register)
    {
        this.register = register;
        dispatchTimer = owner.RegisterGrainTimer(
            static async (@this, _) => await @this.DispatchAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing),
            this,
            new GrainTimerCreationOptions(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan)
            {
                Interleave = interleaveDispatch,
                KeepAlive = true,
            });
    }

    public void SetNextReconcile(OutboxSendOperation<T> batch)
    {
        if (this.batch is not null)
        {
            throw new InvalidOperationException("Reconciliation already in progress. This should not happen because the reconciliation timer should wait for the task to complete before allowing the next tick.");
        }

        this.batch = batch;
        dispatchTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    public async Task DispatchAsync()
    {
        // TODO: dispatch pending items only using registered postmen,
        // track results and attempt counts in-memory, and stop processing if the cancellation token
        // is set.
        await Task.Yield();
    }
}

internal class OutboxProcessor2Reconsiliator<T> where T : notnull
{
    private readonly IGrainTimer reconciliationTimer;
    private readonly Func<ImmutableArray<OutboxMessageEnvelope<T>>, CancellationToken, ValueTask> acknowledgePostedAsync;
    private readonly Func<ImmutableArray<(OutboxMessageEnvelope<T> Envelope, Exception Error, int Attempt)>, CancellationToken, ValueTask> reconcileFailedAsync;
    private OutboxSendOperation<T>? batch;

    public OutboxProcessor2Reconsiliator(
        IGrainBase owner,
        bool interleaveReconciliation,
        Func<ImmutableArray<OutboxMessageEnvelope<T>>, CancellationToken, ValueTask> acknowledgePostedAsync,
        Func<ImmutableArray<(OutboxMessageEnvelope<T> Envelope, Exception Error, int Attempt)>, CancellationToken, ValueTask> reconcileFailedAsync)
    {
        this.acknowledgePostedAsync = acknowledgePostedAsync;
        this.reconcileFailedAsync = reconcileFailedAsync;

        // timer should wait for returned task before allowing next tick,
        // so we can safely update latestBatch when PostInBackgroundAsync completes.
        reconciliationTimer = owner.RegisterGrainTimer(
            static async (@this, _) => await @this.ReconcileOutbox().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing),
            this,
            new GrainTimerCreationOptions(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan)
            {
                Interleave = interleaveReconciliation,
                KeepAlive = true,
            });
    }

    public void SetNextReconcile(OutboxSendOperation<T> batch)
    {
        if (this.batch is not null)
        {
            throw new InvalidOperationException("Reconciliation already in progress. This should not happen because the reconciliation timer should wait for the task to complete before allowing the next tick.");
        }

        this.batch = batch;
        reconciliationTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async Task ReconcileOutbox()
    {
        if (batch is null)
        {
            return;
        }

        if (!batch.Posted.IsDefaultOrEmpty)
        {
            await acknowledgePostedAsync(batch.Posted, batch.CancellationToken);
        }

        if (!batch.Failed.IsDefaultOrEmpty)
        {
            await reconcileFailedAsync(batch.Failed, batch.CancellationToken);
        }
    }
}

internal class OutboxSendOperation<T>(CancellationToken cancellationToken)
     where T : notnull
{
    private TaskCompletionSource dispatchTcs = new();
    private TaskCompletionSource reconcileTcs = new();

    public CancellationToken CancellationToken => cancellationToken;

    public Task DispatchTask => dispatchTcs.Task;

    public Task ReconcileTask => reconcileTcs.Task;

    public ImmutableArray<OutboxMessageEnvelope<T>> Posted { get; private set; }

    public ImmutableArray<(OutboxMessageEnvelope<T> Envelope, Exception Error, int Attempt)> Failed { get; private set; }

    public Outbox<T>? PendingItems { get; set; }
}

public class OutboxProcessor2<T> where T : notnull
{
    private readonly OutboxPostmanRegistry<T> register;
    private readonly OutboxProcessor2Dispatcher<T> dispatcher;
    private readonly OutboxProcessor2Reconsiliator<T> reconsiliator;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly TimeSpan retryDelay;
    private readonly Func<Outbox<T>> pendingItemsAccessor;
    private readonly Channel<OutboxSendOperation<T>> sendChannel = Channel.CreateBounded<OutboxSendOperation<T>>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task sendPrcessorLoop;
    private bool retry

    public OutboxProcessor2(
        IGrainBase owner,
        TimeSpan retryDelay,
        bool interleaveDispatch,
        bool interleaveReconciliation,
        bool keepAlive,
        string reminderName,
        Func<Outbox<T>> pendingItemsAccessor,
        Func<ImmutableArray<OutboxMessageEnvelope<T>>, CancellationToken, ValueTask> acknowledgePostedAsync,
        Func<ImmutableArray<(OutboxMessageEnvelope<T> Envelope, Exception Error, int Attempt)>, CancellationToken, ValueTask> reconcileFailedAsync,
        Outbox<T> outbox)
    {
        register = new OutboxPostmanRegistry<T>();
        dispatcher = new OutboxProcessor2Dispatcher<T>(owner, interleaveDispatch, register);
        reconsiliator = new OutboxProcessor2Reconsiliator<T>(owner, interleaveReconciliation, acknowledgePostedAsync, reconcileFailedAsync);
        this.retryDelay = retryDelay;
        this.pendingItemsAccessor = pendingItemsAccessor;
        sendPrcessorLoop = PostInBackgroundAsync(cancellationTokenSource.Token);
    }

    public async ValueTask PostAsync(CancellationToken cancellationToken = default)
    {
        if (pendingItemsAccessor.Invoke().IsEmpty)
        {
            return;
        }

        var canToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken).Token;
        var outboxSendBatch = new OutboxSendOperation<T>(canToken);
        await sendChannel.Writer.WriteAsync(outboxSendBatch, canToken);
        await outboxSendBatch.DispatchTask;
    }

    public void PostInBackground()
    {
        if (sendChannel.Reader.Count > 0 || pendingItemsAccessor().IsEmpty)
        {
            return;
        }
        sendChannel.Writer.TryWrite(new OutboxSendOperation<T>(cancellationTokenSource.Token));
    }

    internal void AttachToGrain() { }

    public ValueTask ReceiveReminderAsync(string reminderName, TickStatus status)
    {
        if (reminderName != "OutboxProcessorRetry")
        {
            return ValueTask.CompletedTask;
        }

        // using sync post here also ensures that reminder
        // wont fire again until current post run completes
        return PostAsync(cancellationTokenSource.Token);
    }

    private async Task PostInBackgroundAsync(CancellationToken cancellationToken)
    {
        await foreach (var batch in sendChannel.Reader.ReadAllAsync(cancellationToken))
        {
            batch.PendingItems = pendingItemsAccessor();
            dispatcher.SetNextReconcile(batch);
            await batch.DispatchTask;
            reconsiliator.SetNextReconcile(batch);
            await batch.ReconcileTask;

            if (!batch.Failed.IsDefaultOrEmpty && !pendingItemsAccessor().IsEmpty && sendChannel.Reader.Count == 0)
            {
                ScheduleRetry();
            }
        }
    }

    private void ScheduleRetry()
    {

    }
}