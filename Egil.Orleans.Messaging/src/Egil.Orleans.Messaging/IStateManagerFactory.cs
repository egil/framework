namespace Egil.Orleans.Messaging;

/// <summary>
/// Factory contract for constructing provider-specific <see cref="IStateManager{T}"/>
/// instances from Orleans-managed persistent state.
/// </summary>
/// <typeparam name="T">
/// The grain state type.
/// </typeparam>
public interface IStateManagerFactory<T>
    where T : class, IEquatable<T>
{
    /// <summary>
    /// Creates an <see cref="IStateManager{T}"/> for the given
    /// <paramref name="storage"/> facet.
    /// </summary>
    IStateManager<T> Create(IPersistentState<T> storage);
}

/// <summary>
/// Default factory that creates <see cref="DefaultStateManager{T}"/> instances.
/// </summary>
/// <typeparam name="T">
/// The grain state type.
/// </typeparam>
public sealed class DefaultStateManagerFactory<T> : IStateManagerFactory<T>
    where T : class, IEquatable<T>
{
    /// <inheritdoc/>
    public IStateManager<T> Create(IPersistentState<T> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new DefaultStateManager<T>(storage);
    }
}
