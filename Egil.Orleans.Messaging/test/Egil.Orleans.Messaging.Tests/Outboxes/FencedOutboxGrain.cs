using Orleans.Storage;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public interface IFencedOutboxGrain : IGrainWithGuidKey
{
    Task ConfigureAsync(OutboxReminderPolicy policy, bool knownRejection);
    Task<Guid> FailWriteAsync(StorageFailureKind kind, bool persistBeforeFailure, bool staged = false, bool unreadable = false);
    Task FailTwoManagersAsync(bool conflictFirst);
    Task FailWithBrokenAccessorAsync(StorageFailureKind? kind);
    Task<FencedOutboxObservation> ObserveAsync();
    Task<FencedOutboxObservation> RecoverAsync();
}

[GenerateSerializer]
public sealed record FencedOutboxState
{
    [Id(0)] public Outbox<string> Outbox { get; init; } = [];
    [Id(1)] public int Delivered { get; init; }
    [Id(2)] public OutboxReminderPolicy Policy { get; init; }
    [Id(3)] public bool KnownRejection { get; init; }
}

[GenerateSerializer]
public sealed record FencedOutboxObservation(
    [property: Id(0)] int Delivered,
    [property: Id(1)] int Pending,
    [property: Id(2)] Guid Activation);

public sealed class FencedOutboxGrain(
    [PersistentState("state", "Payload")] IPersistentState<FencedOutboxState> storage,
    [PersistentState("other", "Payload")] IPersistentState<FencedOutboxState> otherStorage,
    OutboxDeactivationProbe deactivations) : Grain, IFencedOutboxGrain, IOutboxGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly FailingStorage failingStorage = new(storage);
    private IStateManager<FencedOutboxState> manager = null!;
    private OutboxProcessor<string> processor = null!;
    private Exception? accessorFailure;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        manager = storage.State.KnownRejection
            ? new RejectingStateManager(failingStorage, GrainContext)
            : new DefaultStateManager<FencedOutboxState>(failingStorage, static () => new(), grainContext: GrainContext);
        processor = this.RegisterOutboxProcessor(() => accessorFailure is { } failure
            ? throw failure : manager.State.Outbox, options =>
        {
            options.ReminderPolicy = manager.State.Policy;
            options.ActiveReminderPeriod = TimeSpan.FromMinutes(10);
            options.IdleReminderPeriod = TimeSpan.FromHours(2);
            options.AcknowledgePostedAsync = async (items, token) =>
                await manager.WriteAsync(manager.State with
                {
                    Outbox = manager.State.Outbox.RemoveRange(items),
                    Delivered = manager.State.Delivered + items.Length,
                }, token);
        }).AddPostman<string>(static _ => Task.CompletedTask);
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task ConfigureAsync(OutboxReminderPolicy policy, bool knownRejection)
    {
        await manager.WriteAsync(manager.State with { Policy = policy, KnownRejection = knownRejection });
        DeactivateOnIdle();
    }

    public async Task<Guid> FailWriteAsync(StorageFailureKind kind, bool persistBeforeFailure, bool staged = false, bool unreadable = false)
    {
        deactivations.Observe(GrainContext);
        await FailMutationAsync(manager, failingStorage, kind, persistBeforeFailure, staged);
        if (unreadable)
            accessorFailure = new StateManagerFencedException(kind, failingStorage.Failure!);
        return activation;
    }

    public async Task FailTwoManagersAsync(bool conflictFirst)
    {
        await FailWriteAsync(conflictFirst ? StorageFailureKind.Conflict : StorageFailureKind.UnknownOutcome, false);
        var otherFailure = new FailingStorage(otherStorage);
        var otherManager = new DefaultStateManager<FencedOutboxState>(otherFailure, static () => new(), grainContext: GrainContext);
        await FailMutationAsync(otherManager, otherFailure,
            conflictFirst ? StorageFailureKind.UnknownOutcome : StorageFailureKind.Conflict, false, false);
    }

    public async Task FailWithBrokenAccessorAsync(StorageFailureKind? kind)
    {
        accessorFailure = new FormatException("Invalid local outbox state.");
        if (kind is { } failureKind)
        {
            await FailWriteAsync(failureKind, persistBeforeFailure: failureKind == StorageFailureKind.UnknownOutcome);
        }
        else
        {
            deactivations.Observe(GrainContext);
            DeactivateOnIdle();
        }
    }

    private static async Task FailMutationAsync(IStateManager<FencedOutboxState> stateManager,
        FailingStorage stateStorage, StorageFailureKind kind, bool persistBeforeFailure, bool staged)
    {
        stateStorage.Failure = kind == StorageFailureKind.Conflict
            ? new InconsistentStateException("Stale ETag.")
            : new IOException("Storage failed.");
        stateStorage.PersistBeforeFailure = persistBeforeFailure;
        var candidate = stateManager.State with { Outbox = stateManager.State.Outbox.Add("pending") };
        if (staged)
            stateManager.State = candidate;
        try
        {
            await stateManager.WriteAsync(candidate);
        }
        catch (Exception exception) when (ReferenceEquals(exception, stateStorage.Failure))
        {
            // Only the manager requests deactivation here. The probe waits for
            // Orleans to finish the actual shutdown and outbox lifecycle hook.
        }
    }

    public Task<FencedOutboxObservation> ObserveAsync() => Task.FromResult(
        new FencedOutboxObservation(manager.State.Delivered, manager.State.Outbox.Count, activation));

    public async Task<FencedOutboxObservation> RecoverAsync()
    {
        // Enter through Orleans' reminder contract without waiting ten minutes.
        // Both posting and acknowledgement still run on the activation scheduler.
        await ((IRemindable)this).ReceiveReminder(processor.ReminderName, default);
        await processor.PostAsync();
        return await ObserveAsync();
    }

    private sealed class RejectingStateManager(IPersistentState<FencedOutboxState> state, IGrainContext context)
        : StateManagerBase<FencedOutboxState>(state, static () => new(), grainContext: context)
    {
        protected override StorageFailureKind ClassifyWriteFailure(Exception exception) => StorageFailureKind.DidNotPersist;
    }

    // Keep durable writes and activation reloads in the real memory provider.
    // Inject failure only at its response boundary, before or after persistence.
    private sealed class FailingStorage(IPersistentState<FencedOutboxState> inner) : IPersistentState<FencedOutboxState>
    {
        public FencedOutboxState State { get => inner.State; set => inner.State = value; }
        public string? Etag => inner.Etag;
        public bool RecordExists => inner.RecordExists;
        public Exception? Failure { get; set; }
        public bool PersistBeforeFailure { get; set; }
        public Task ReadStateAsync() => inner.ReadStateAsync();
        public Task ReadStateAsync(CancellationToken cancellationToken) => inner.ReadStateAsync(cancellationToken);
        public Task ClearStateAsync() => inner.ClearStateAsync();
        public Task ClearStateAsync(CancellationToken cancellationToken) => inner.ClearStateAsync(cancellationToken);
        public Task WriteStateAsync() => WriteStateAsync(CancellationToken.None);

        public async Task WriteStateAsync(CancellationToken cancellationToken)
        {
            if (Failure is null || PersistBeforeFailure)
                await inner.WriteStateAsync(cancellationToken);
            if (Failure is { } failure)
                throw failure;
        }
    }
}
