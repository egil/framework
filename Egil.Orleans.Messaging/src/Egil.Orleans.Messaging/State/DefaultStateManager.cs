using Orleans.Storage;

namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Default <see cref="IStateManager{T}"/> implementation for general storage providers.
/// </summary>
/// <typeparam name="T">
/// <inheritdoc cref="IStateManager{T}" path="/typeparam"/>
/// </typeparam>
public sealed class DefaultStateManager<T> : StateManagerBase<T>
    where T : class, IEquatable<T>
{
    /// <summary>
    /// Creates a manager over the grain's persistent state facet. Recognizes
    /// <see cref="InconsistentStateException"/> through exception wrappers as
    /// <see cref="StorageFailureKind.Conflict"/>. Other failures have an
    /// <see cref="StorageFailureKind.UnknownOutcome"/> because general providers
    /// give no reliable signal that an operation was rejected before persisting.
    /// </summary>
    public DefaultStateManager(
        IPersistentState<T> storage,
        Func<T> createInitialState,
        Action<T>? configureState = null)
        : base(storage, createInitialState, configureState)
    {
    }

    /// <inheritdoc/>
    protected override StorageFailureKind ClassifyWriteFailure(Exception exception) => ClassifyFailure(exception);

    /// <inheritdoc/>
    protected override StorageFailureKind ClassifyClearFailure(Exception exception) => ClassifyFailure(exception);

    private static StorageFailureKind ClassifyFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Wrapping a concurrency rejection must not turn it into a lost response
        // that equality can hide. An aggregate is different: a rejected retry
        // cannot establish the outcome of an uncertain sibling attempt.
        return exception switch
        {
            InconsistentStateException => StorageFailureKind.Conflict,
            AggregateException aggregate => aggregate.InnerExceptions.Count > 0
                && aggregate.InnerExceptions.All(inner => ClassifyFailure(inner) is StorageFailureKind.Conflict)
                    ? StorageFailureKind.Conflict
                    : StorageFailureKind.UnknownOutcome,
            { InnerException: { } inner } => ClassifyFailure(inner),
            _ => StorageFailureKind.UnknownOutcome
        };
    }
}
