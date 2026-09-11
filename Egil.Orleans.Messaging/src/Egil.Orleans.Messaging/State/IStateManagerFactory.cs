namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Factory contract for constructing provider-specific <see cref="IStateManager{T}"/>
/// instances from Orleans-managed persistent state.
/// </summary>
public interface IStateManagerFactory
{
    /// <summary>
    /// Creates an <see cref="IStateManager{T}"/> for the given
    /// <paramref name="storage"/> facet.
    /// </summary>
    IStateManager<T> Create<T>(IPersistentState<T> storage, Func<T> createInitialState, Action<T>? configureState = null)
        where T : class, IEquatable<T>;
}
