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
        /// Registers a state manager against a keyed <see cref="IStateManagerFactory"/>,
        /// letting the state type answer for an absent record.
        /// </summary>
        /// <remarks>
        /// An absent record resolves to <see cref="IStateDefault{TSelf}.CreateDefault"/> when
        /// <typeparamref name="TState"/> implements it, and to <c>new TState()</c> otherwise.
        /// Use the overload taking a state factory to override both. A state type with
        /// neither is rejected at registration, naming the state type and the grain.
        /// </remarks>
        /// <typeparam name="TState">The grain state type.</typeparam>
        /// <param name="storageName">Logical storage name used as the keyed DI registration key.</param>
        /// <param name="storage">The Orleans-managed persistent state facet.</param>
        /// <param name="configureState">
        /// Configures runtime dependencies on each adopted state instance, after
        /// <see cref="IConfigurableState.Configure"/> when the state type implements it. This
        /// callback must not change persisted business values or perform storage I/O.
        /// </param>
        public IStateManager<TState> RegisterStateManager<TState>(
            string storageName,
            IPersistentState<TState> storage,
            Action<TState>? configureState = null)
            where TState : class, IEquatable<TState>
        {
            ArgumentNullException.ThrowIfNull(grain);
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
            ArgumentNullException.ThrowIfNull(storage);

            return RegisterStateManagerCore(
                grain.GrainContext,
                storageName,
                storage,
                grain.GetType(),
                createInitialState: null,
                configureState,
                grain.GrainContext.GrainInstance is null ? grain.GrainContext.ObservableLifecycle : null);
        }

        /// <summary>
        /// Registers a state manager against the silo's default
        /// <see cref="IStateManagerFactory"/>, letting the state type answer for an absent
        /// record.
        /// </summary>
        /// <remarks>
        /// An absent record resolves to <see cref="IStateDefault{TSelf}.CreateDefault"/> when
        /// <typeparamref name="TState"/> implements it, and to <c>new TState()</c> otherwise.
        /// Use the overload taking a state factory to override both. A state type with
        /// neither is rejected at registration, naming the state type and the grain.
        /// </remarks>
        /// <typeparam name="TState">The grain state type.</typeparam>
        /// <param name="storage">The Orleans-managed persistent state facet.</param>
        /// <param name="configureState">
        /// Configures runtime dependencies on each adopted state instance, after
        /// <see cref="IConfigurableState.Configure"/> when the state type implements it. This
        /// callback must not change persisted business values or perform storage I/O.
        /// </param>
        public IStateManager<TState> RegisterStateManager<TState>(
            IPersistentState<TState> storage,
            Action<TState>? configureState = null)
            where TState : class, IEquatable<TState>
        {
            ArgumentNullException.ThrowIfNull(grain);
            ArgumentNullException.ThrowIfNull(storage);

            return RegisterStateManagerCore(
                grain.GrainContext,
                null,
                storage,
                grain.GetType(),
                createInitialState: null,
                configureState,
                grain.GrainContext.GrainInstance is null ? grain.GrainContext.ObservableLifecycle : null);
        }

        /// <summary>
        /// Creates an <see cref="IStateManager{T}"/> for the given grain using the silo's
        /// default <see cref="IStateManagerFactory"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The facet already carries its names, so this overload asks for neither:
        /// <code>
        /// public MyGrain([PersistentState("state", "Default")] IPersistentState&lt;MyState&gt; storage)
        /// {
        ///     stateManager = this.RegisterStateManager(storage);
        /// }
        /// </code>
        /// Register the factory with <c>AddDefaultStateManager()</c>. Use the overloads
        /// that take a storage name when different storage providers need different
        /// failure handling, since the factory is what classifies their failures.
        /// </para>
        /// </remarks>
        /// <typeparam name="TState">The grain state type.</typeparam>
        /// <param name="storage">The Orleans-managed persistent state facet.</param>
        /// <param name="createInitialState">
        /// Creates state when no persisted record exists; does not write it. Overrides
        /// <see cref="IStateDefault{TSelf}.CreateDefault"/> when the state type implements it.
        /// </param>
        /// <param name="configureState">
        /// Configures runtime dependencies on each adopted state instance, after
        /// <see cref="IConfigurableState.Configure"/> when the state type implements it. This
        /// callback must not change persisted business values or perform storage I/O.
        /// </param>
        /// <returns>An <see cref="IStateManager{T}"/> instance.</returns>
        public IStateManager<TState> RegisterStateManager<TState>(
            IPersistentState<TState> storage,
            Func<TState> createInitialState,
            Action<TState>? configureState = null)
            where TState : class, IEquatable<TState>
        {
            ArgumentNullException.ThrowIfNull(grain);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(createInitialState);

            return RegisterStateManagerCore(
                grain.GrainContext,
                storageName: null,
                storage,
                grain.GetType(),
                createInitialState,
                configureState,
                grain.GrainContext.GrainInstance is null ? grain.GrainContext.ObservableLifecycle : null);
        }

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
        /// <param name="createInitialState">
        /// Creates state when no persisted record exists; does not write it. Overrides
        /// <see cref="IStateDefault{TSelf}.CreateDefault"/> when the state type implements it.
        /// </param>
        /// <param name="configureState">
        /// Configures runtime dependencies on each adopted state instance, after
        /// <see cref="IConfigurableState.Configure"/> when the state type implements it. This
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
                grain.GrainContext,
                storageName,
                storage,
                grain.GetType(),
                createInitialState,
                configureState,
                grain.GrainContext.GrainInstance is null ? grain.GrainContext.ObservableLifecycle : null);
        }
    }

    internal static IStateManager<TState> RegisterStateManagerCore<TState>(
        IGrainContext grainContext,
        string? storageName,
        IPersistentState<TState> storage,
        Type grainType,
        Func<TState>? createInitialState,
        Action<TState>? configureState = null,
        IGrainLifecycle? lifecycle = null)
        where TState : class, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(grainContext);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(grainType);

        var activationServices = grainContext.ActivationServices;

        // A null storage name means the caller did not choose a provider-specific factory,
        // so the unkeyed registration is the one to use.
        var factory = storageName is null
            ? activationServices.GetService<IStateManagerFactory>()
            : activationServices.GetKeyedService<IStateManagerFactory>(storageName);

        if (factory is null)
        {
            throw new InvalidOperationException(storageName is null
                ? "No default IStateManagerFactory registration was found for state type " +
                  $"'{typeof(TState).FullName}', grain type '{grainType.FullName}'. " +
                  "Register one via AddDefaultStateManager() or AddStateManagerFactory(Type), " +
                  "or name a storage provider to select a keyed registration."
                : $"No keyed IStateManagerFactory registration was found for storage name '{storageName}', " +
                  $"state type '{typeof(TState).FullName}', grain type '{grainType.FullName}'. " +
                  "Register one via AddDefaultStateManager(...) or AddStateManagerFactory(...).");
        }

        var initialState = ResolveInitialState(grainContext, grainType, createInitialState);
        var configure = ComposeConfiguration<TState>(grainContext, configureState);

        return lifecycle is null
            ? factory.Create(storage, initialState, configure)
            : new ActivationStateManager<TState>(lifecycle,
                () => factory.Create(storage, initialState, configure));
    }

    private static Func<TState> ResolveInitialState<TState>(
        IGrainContext grainContext,
        Type grainType,
        Func<TState>? createInitialState)
        where TState : class, IEquatable<TState>
    {
        // An explicit factory is the grain overriding what the state type says about
        // itself, so it wins. Without one the state type answers, and only then does a
        // parameterless constructor stand in.
        if (createInitialState is not null)
        {
            return createInitialState;
        }

        if (StateContract<TState>.CreateDefault is { } createDefault)
        {
            return () => createDefault(grainContext);
        }

        if (StateContract<TState>.CreateInstance is { } createInstance)
        {
            return createInstance;
        }

        throw new InvalidOperationException(
            $"State type '{typeof(TState).FullName}' for grain type '{grainType.FullName}' has no public " +
            $"parameterless constructor, so an absent storage record cannot be represented. Implement " +
            $"IStateDefault<{typeof(TState).Name}> on it, or pass a state factory to RegisterStateManager.");
    }

    private static Action<TState>? ComposeConfiguration<TState>(
        IGrainContext grainContext,
        Action<TState>? configureState)
        where TState : class, IEquatable<TState>
    {
        if (StateContract<TState>.Configure is not { } configureStateType)
        {
            return configureState;
        }

        if (configureState is null)
        {
            return state => configureStateType(state, grainContext);
        }

        // The state type's own wiring is the baseline every holder of that state needs, so
        // it runs first and the grain's callback can build on or override what it set.
        return state =>
        {
            configureStateType(state, grainContext);
            configureState(state);
        };
    }
}
