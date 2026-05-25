namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Default <see cref="IStateManager{T}"/> implementation for general storage providers.
/// </summary>
public sealed class DefaultStateManager<T> : StateManagerBase<T>
    where T : class, IEquatable<T>
{
    public DefaultStateManager(IPersistentState<T> storage)
        : base(storage)
    {
    }

    /// <inheritdoc/>
    protected override StorageFailureKind ClassifyWriteFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return StorageFailureKind.UnknownOutcome;
    }
}