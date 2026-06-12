namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Default factory that creates <see cref="DefaultStateManager{T}"/> instances.
/// </summary>
public sealed class DefaultStateManagerFactory : IStateManagerFactory
{
    /// <inheritdoc/>
    public IStateManager<T> Create<T>(IPersistentState<T> storage)
        where T : class, IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new DefaultStateManager<T>(storage);
    }
}