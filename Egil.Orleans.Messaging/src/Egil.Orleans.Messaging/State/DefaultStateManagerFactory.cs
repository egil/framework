namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Default factory that creates <see cref="DefaultStateManager{T}"/> instances.
/// </summary>
public sealed class DefaultStateManagerFactory : IStateManagerFactory
{
    /// <inheritdoc/>
    public IStateManager<T> Create<T>(IPersistentState<T> storage, Func<T> createInitialState, Action<T>? configureState = null)
        where T : class, IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new DefaultStateManager<T>(storage, createInitialState, configureState);
    }
}