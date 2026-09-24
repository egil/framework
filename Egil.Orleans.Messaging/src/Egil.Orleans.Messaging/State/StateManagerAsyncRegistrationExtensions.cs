using Egil.Orleans.Messaging.State;

namespace Orleans;

public static partial class StateManagerExtensions
{
    extension<TGrain>(TGrain grain) where TGrain : IGrainBase
    {
        /// <summary>Registers a hydrated manager in OnActivateAsync and awaits its initial read handlers.</summary>
        public Task<IStateManager<TState>> RegisterStateManagerAsync<TState>(
            IPersistentState<TState> storage, Action<StateManagerHooks<TState>> configureHooks,
            Action<TState>? configureState = null, CancellationToken cancellationToken = default)
            where TState : class, IEquatable<TState>
            => RegisterInitializedManagerAsync(grain, null, storage, null, configureState, configureHooks, cancellationToken);

        /// <summary>Registers a hydrated manager with a keyed factory and awaits its initial read handlers.</summary>
        public Task<IStateManager<TState>> RegisterStateManagerAsync<TState>(
            string storageName, IPersistentState<TState> storage, Action<StateManagerHooks<TState>> configureHooks,
            Action<TState>? configureState = null, CancellationToken cancellationToken = default)
            where TState : class, IEquatable<TState>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
            return RegisterInitializedManagerAsync(grain, storageName, storage, null, configureState, configureHooks, cancellationToken);
        }

        /// <summary>Registers a hydrated manager with an explicit default and awaits its initial read handlers.</summary>
        public Task<IStateManager<TState>> RegisterStateManagerAsync<TState>(
            IPersistentState<TState> storage, Func<TState> createInitialState, Action<StateManagerHooks<TState>> configureHooks,
            Action<TState>? configureState = null, CancellationToken cancellationToken = default)
            where TState : class, IEquatable<TState>
        {
            ArgumentNullException.ThrowIfNull(createInitialState);
            return RegisterInitializedManagerAsync(grain, null, storage, createInitialState, configureState, configureHooks, cancellationToken);
        }

        /// <summary>Registers a hydrated manager with a keyed factory and explicit default, awaiting initial handlers.</summary>
        public Task<IStateManager<TState>> RegisterStateManagerAsync<TState>(
            string storageName, IPersistentState<TState> storage, Func<TState> createInitialState,
            Action<StateManagerHooks<TState>> configureHooks, Action<TState>? configureState = null,
            CancellationToken cancellationToken = default)
            where TState : class, IEquatable<TState>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
            ArgumentNullException.ThrowIfNull(createInitialState);
            return RegisterInitializedManagerAsync(grain, storageName, storage, createInitialState, configureState, configureHooks, cancellationToken);
        }
    }

    private static async Task<IStateManager<TState>> RegisterInitializedManagerAsync<TState>(
        IGrainBase grain, string? storageName, IPersistentState<TState> storage, Func<TState>? createInitialState,
        Action<TState>? configureState, Action<StateManagerHooks<TState>> configureHooks, CancellationToken cancellationToken)
        where TState : class, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(grain);
        var hooks = StateManagerHooks<TState>.Create(configureHooks);
        cancellationToken.ThrowIfCancellationRequested();
        if (grain.GrainContext.GrainInstance is null)
        {
            throw new InvalidOperationException("Use RegisterStateManager in the constructor and RegisterStateManagerAsync in OnActivateAsync after hydration.");
        }

        var manager = RegisterStateManagerCore(grain.GrainContext, storageName, storage, grain.GetType(),
            createInitialState, configureState);
        manager.ConfigureHooks(hooks.CopyTo);
        await manager.NotifyInitialReadAsync(cancellationToken);
        return manager;
    }
}
