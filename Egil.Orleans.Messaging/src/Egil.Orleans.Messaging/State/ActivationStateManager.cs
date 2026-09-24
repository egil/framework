namespace Egil.Orleans.Messaging.State;

// The stable constructor-time handle delays provider-specific manager creation.
// Those managers may inspect storage in their constructors, so deferring only
// the default factory would still let custom factories capture unhydrated state.
internal sealed class ActivationStateManager<T> : IStateManager<T>
    where T : class, IEquatable<T>
{
    private IStateManager<T>? manager;
    private StateManagerHooks<T> hooks = new();
    private readonly Func<bool> recordExists;
    private readonly StateManagerHookDispatcher<T> initialReadDispatcher = new();

    public void ConfigureHooks(Action<StateManagerHooks<T>> configure)
    {
        var snapshot = StateManagerHooks<T>.Create(configure);
        manager?.ConfigureHooks(snapshot.CopyTo);
        hooks = snapshot;
    }

    public ActivationStateManager(Func<IStateManager<T>> create, Func<bool> recordExists, IGrainLifecycle? lifecycle)
    {
        this.recordExists = recordExists;
        if (lifecycle is null)
        {
            manager = create();
            return;
        }

        // A distinct stage is essential: callbacks at SetupState can run in
        // parallel with Orleans' persistent-state hydration.
        lifecycle.Subscribe(GetType().FullName!, GrainLifecycleStage.SetupState + 1, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            manager = create();
            manager.ConfigureHooks(hooks.CopyTo);
            return NotifyInitialReadAsync(cancellationToken);
        });
    }

    // Only registration owns initial notification: a lifecycle subscription invokes
    // this after hydration, or async registration awaits it before returning the handle.
    // ConfigureHooks never calls it, so ad-hoc configuration cannot replay a load.
    internal Task NotifyInitialReadAsync(CancellationToken cancellationToken)
        => initialReadDispatcher.InvokeAsync(hooks, Manager.State, StateManagerOperation.Read, recordExists(), cancellationToken);

    private IStateManager<T> Manager => manager
        ?? throw new InvalidOperationException("State is not initialized. Access it after persistent state hydration, starting in OnActivateAsync.");

    // The underlying manager guards its normal operation hooks. The registration
    // wrapper owns the initial hook, so it must guard calls through this same handle.
    private IStateManager<T> OperationManager => initialReadDispatcher.IsInvoking
        ? throw new InvalidOperationException("Storage operations on this manager cannot be called from a lifecycle handler.")
        : Manager;

    public T State
    {
        get => Manager.State;
        set => Manager.State = value;
    }

    public bool HasUnsavedChanges => Manager.HasUnsavedChanges;

    public Task ReadAsync(CancellationToken cancellationToken = default) => OperationManager.ReadAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => OperationManager.SaveChangesAsync(cancellationToken);

    public Task WriteAsync(T newState, CancellationToken cancellationToken = default) => OperationManager.WriteAsync(newState, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => OperationManager.ClearAsync(cancellationToken);
}
