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
    /// Creates a manager over the grain's persistent state facet. Treats all
    /// write failures as <see cref="StorageFailureKind.UnknownOutcome"/>
    /// because general providers give no reliable signal that a write was
    /// rejected before persisting.
    /// </summary>
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