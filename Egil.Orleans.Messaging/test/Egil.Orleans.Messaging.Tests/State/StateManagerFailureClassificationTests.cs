using System.Text;
using Orleans.Serialization;
using Orleans.Storage;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerFailureClassificationTests
{
    [Theory]
    [InlineData(false, false, StorageFailureKind.Conflict)]
    [InlineData(false, false, StorageFailureKind.DidNotPersist)]
    [InlineData(false, false, StorageFailureKind.UnknownOutcome)]
    [InlineData(true, false, StorageFailureKind.Conflict)]
    [InlineData(true, false, StorageFailureKind.DidNotPersist)]
    [InlineData(true, false, StorageFailureKind.UnknownOutcome)]
    [InlineData(false, true, StorageFailureKind.Conflict)]
    [InlineData(false, true, StorageFailureKind.DidNotPersist)]
    [InlineData(false, true, StorageFailureKind.UnknownOutcome)]
    [InlineData(true, true, StorageFailureKind.Conflict)]
    [InlineData(true, true, StorageFailureKind.DidNotPersist)]
    [InlineData(true, true, StorageFailureKind.UnknownOutcome)]
    public async Task Both_policies_classify_the_original_failure_once(
        bool clear, bool readBack, StorageFailureKind kind)
    {
        var failure = new IOException("Storage failed.");
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { MutationFailure = failure };
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new RecoveryGrainContext(services);
        DeactivationReason? reason = null;
        context.OnDeactivate = (value, _) => reason = value;
        var manager = new ClassifyingManager(storage, kind, readBack, context);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => MutateAsync(manager, clear)));

        Assert.Equal(clear ? 0 : 1, manager.WriteClassifications);
        Assert.Equal(clear ? 1 : 0, manager.ClearClassifications);
        Assert.Same(failure, manager.ClassifiedFailure);
        if (readBack)
        {
            Assert.Equal("stored", manager.State.Value);
            Assert.Equal(kind == StorageFailureKind.DidNotPersist ? 0 : 1, storage.Reads);
            Assert.Null(reason);
        }
        else
        {
            var fenced = Assert.Throws<StateManagerFencedException>(() => manager.State = new("rejected"));
            Assert.Equal(kind, fenced.FailureKind);
            Assert.Same(failure, fenced.InnerException);
            Assert.Equal(0, storage.Reads);
            Assert.Equal(DeactivationReasonCode.ApplicationError, reason!.Value.ReasonCode);
            Assert.Same(failure, reason.Value.Exception);
            Assert.Equal($"State manager for '{typeof(Snapshot).FullName}' was fenced after " +
                $"{(clear ? "ClearAsync" : "WriteAsync")} failed. Storage failure: {kind}.", reason.Value.Description);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_broken_classifier_preserves_fencing_without_changing_read_back_behavior(bool clear, bool readBack)
    {
        var failure = new IOException("Storage failed.");
        var classifierFailure = new InvalidOperationException("Classifier failed.");
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { MutationFailure = failure };
        var manager = new ClassifyingManager(storage, StorageFailureKind.Conflict, readBack)
        {
            ClassificationFailure = classifierFailure,
        };

        var actual = await Record.ExceptionAsync(() => MutateAsync(manager, clear));

        Assert.Same(readBack ? classifierFailure : failure, actual);
        Assert.Equal(1, manager.WriteClassifications + manager.ClearClassifications);
        Assert.Equal(0, storage.Reads);
        if (!readBack)
        {
            var fenced = Assert.Throws<StateManagerFencedException>(() => manager.State = new("rejected"));
            Assert.Equal(StorageFailureKind.UnknownOutcome, fenced.FailureKind);
            Assert.Same(failure, fenced.InnerException);
        }
    }

    [Theory]
    [InlineData(false, 0, StorageFailureKind.Conflict)]
    [InlineData(true, 0, StorageFailureKind.Conflict)]
    [InlineData(false, 1, StorageFailureKind.Conflict)]
    [InlineData(true, 1, StorageFailureKind.Conflict)]
    [InlineData(false, 2, StorageFailureKind.UnknownOutcome)]
    [InlineData(true, 2, StorageFailureKind.UnknownOutcome)]
    public async Task Default_classifier_preserves_wrapped_and_aggregate_failure_evidence(
        bool clear, int scenario, StorageFailureKind expected)
    {
        var conflict = new InconsistentStateException("Stale ETag.");
        Exception failure = scenario switch
        {
            0 => new InvalidOperationException("Wrapped.", conflict),
            1 => new AggregateException(conflict, new InvalidOperationException("Wrapped.", conflict)),
            _ => new AggregateException(conflict, new IOException("Unknown outcome.")),
        };
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { MutationFailure = failure };
        var manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));

        Assert.Same(failure, await Record.ExceptionAsync(() => MutateAsync(manager, clear)));

        var fenced = Assert.Throws<StateManagerFencedException>(() => manager.State = new("rejected"));
        Assert.Equal(expected, fenced.FailureKind);
        Assert.Same(failure, fenced.InnerException);
        Assert.Equal(0, storage.Reads);
    }

    [Theory]
    [InlineData(StorageFailureKind.Conflict)]
    [InlineData(StorageFailureKind.DidNotPersist)]
    public void Fencing_exception_round_trips_through_Orleans(StorageFailureKind kind)
    {
        using var services = new ServiceCollection()
            .AddSerializer(builder => builder.AddAssembly(typeof(StateManagerFencedException).Assembly))
            .BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        Exception original = new StateManagerFencedException(kind, new InconsistentStateException("Stale ETag."));

        var serialized = serializer.SerializeToArray(original);
        var restored = Assert.IsType<StateManagerFencedException>(serializer.Deserialize<Exception>(serialized));

        Assert.Contains("egil.orleans.messaging.StateManagerFencedException", Encoding.UTF8.GetString(serialized), StringComparison.Ordinal);
        Assert.Equal(kind, restored.FailureKind);
        Assert.Equal(original.Message, restored.Message);
        Assert.Equal("Stale ETag.", Assert.IsType<InconsistentStateException>(restored.InnerException).Message);
    }

    private static Task MutateAsync(IStateManager<Snapshot> manager, bool clear) => clear
        ? manager.ClearAsync(TestContext.Current.CancellationToken)
        : manager.WriteAsync(new("candidate"), TestContext.Current.CancellationToken);

    private sealed record Snapshot(string Value);

    private sealed class ClassifyingManager(
        RecoveryStorage<Snapshot> storage, StorageFailureKind kind, bool readBack, IGrainContext? context = null)
        : StateManagerBase<Snapshot>(storage, static () => new("default"),
            recoveryPolicy: readBack ? StateRecoveryPolicy.ReadBack : StateRecoveryPolicy.FenceAndDeactivate,
            grainContext: context)
    {
        public int WriteClassifications { get; private set; }
        public int ClearClassifications { get; private set; }
        public Exception? ClassifiedFailure { get; private set; }
        public Exception? ClassificationFailure { get; init; }

        protected override StorageFailureKind ClassifyWriteFailure(Exception exception)
        {
            WriteClassifications++;
            return Classify(exception);
        }

        protected override StorageFailureKind ClassifyClearFailure(Exception exception)
        {
            ClearClassifications++;
            return Classify(exception);
        }

        private StorageFailureKind Classify(Exception exception)
        {
            ClassifiedFailure = exception;
            if (ClassificationFailure is { } failure)
                throw failure;
            return kind;
        }
    }
}
