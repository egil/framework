using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.State;

namespace Orleans;

/// <summary>
/// Extension methods for wiring <see cref="IStateManager{T}"/> into a grain's
/// activation lifecycle.
/// </summary>
public static class StateManagerExtensions
{
    extension<TGrain>(TGrain grain)
        where TGrain : IGrainBase
    {
        /// <summary>
        /// Registers a state manager using a new state instance when no record exists.
        /// </summary>
        public IStateManager<TState> RegisterStateManager<TState>(
            string storageName,
            IPersistentState<TState> storage)
            where TState : class, IEquatable<TState>, new() =>
            grain.RegisterStateManager(storageName, storage, static () => new TState());

        /// <summary>
        /// Creates an <see cref="IStateManager{T}"/> for the given grain using
        /// a keyed <see cref="IStateManagerFactory"/> registration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Call site:</b> Call once in the constructor to assign a readonly field,
        /// or in <c>OnActivateAsync</c> after hydration. Constructor registration
        /// defers factory invocation until persistent state has loaded:
        /// <code>
        /// private readonly IStateManager&lt;MyState&gt; stateManager;
        ///
        /// public MyGrain([PersistentState("state")] IPersistentState&lt;MyState&gt; storage)
        /// {
        ///     stateManager = this.RegisterStateManager("state", storage);
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// <b>Registration:</b> A keyed
        /// <see cref="IStateManagerFactory"/> must be registered for
        /// <paramref name="storageName"/> via the provided registration helpers.
        /// Missing registrations fail fast with a descriptive
        /// <see cref="InvalidOperationException"/>.
        /// </para>
        /// </remarks>
        /// <typeparam name="TState">
        /// The grain state type.
        /// </typeparam>
        /// <param name="storageName">
        /// Logical storage name used as the keyed DI registration key.
        /// </param>
        /// <param name="storage">
        /// The Orleans-managed persistent state facet.
        /// </param>
        /// <param name="createInitialState">Creates state when no persisted record exists; does not write it.</param>
        /// <param name="configureState">
        /// Configures runtime dependencies on each adopted state instance. This
        /// callback must not change persisted business values or perform storage I/O.
        /// </param>
        /// <returns>
        /// A keyed <see cref="IStateManager{T}"/> instance.
        /// </returns>
        public IStateManager<TState> RegisterStateManager<TState>(
            string storageName,
            IPersistentState<TState> storage,
            Func<TState> createInitialState,
            Action<TState>? configureState = null)
            where TState : class, IEquatable<TState>
        {
            ArgumentNullException.ThrowIfNull(grain);
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(createInitialState);

            return RegisterStateManagerCore(
                grain.GrainContext.ActivationServices,
                storageName,
                storage,
                grain.GetType(),
                createInitialState,
                configureState,
                grain.GrainContext.GrainInstance is null ? grain.GrainContext.ObservableLifecycle : null);
        }
    }

    internal static IStateManager<TState> RegisterStateManagerCore<TState>(
        IServiceProvider activationServices,
        string storageName,
        IPersistentState<TState> storage,
        Type grainType,
        Func<TState> createInitialState,
        Action<TState>? configureState = null,
        IGrainLifecycle? lifecycle = null)
        where TState : class, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(activationServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(grainType);

        var factory = activationServices.GetKeyedService<IStateManagerFactory>(storageName);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"No keyed IStateManagerFactory registration was found for storage name '{storageName}', " +
                $"state type '{typeof(TState).FullName}', grain type '{grainType.FullName}'. " +
                "Register one via AddDefaultStateManager(...) or AddStateManagerFactory(...).");
        }

        ArgumentNullException.ThrowIfNull(createInitialState);
        return lifecycle is null
            ? factory.Create(storage, createInitialState, configureState)
            : new ActivationStateManager<TState>(lifecycle,
                () => factory.Create(storage, createInitialState, configureState));
    }
}
