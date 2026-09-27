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
    /// <remarks>
    /// Registration resolves options after hydration, applying global, factory, then
    /// grain-local overrides. Factories should remain stateless and forward the policy
    /// and context to each manager rather than retaining mutable configuration.
    /// </remarks>
    /// <param name="storage">The hydrated Orleans storage facet.</param>
    /// <param name="createInitialState">Creates a default for absent storage without persisting it.</param>
    /// <param name="options">The validated, isolated configuration snapshot for this manager.</param>
    /// <param name="configureState">Optional runtime wiring for each adopted state.</param>
    /// <param name="grainContext">Enables deactivation when fencing; null permits fencing only.</param>
    IStateManager<T> Create<T>(IPersistentState<T> storage, Func<T> createInitialState, StateManagerOptions options, Action<T>? configureState = null, IGrainContext? grainContext = null)
        where T : class, IEquatable<T>;
}
