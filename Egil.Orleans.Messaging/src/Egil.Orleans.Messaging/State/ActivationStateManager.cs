namespace Egil.Orleans.Messaging.State;

// The stable constructor-time handle delays provider-specific manager creation.
// Those managers may inspect storage in their constructors, so deferring only
// the default factory would still let custom factories capture unhydrated state.
internal sealed class ActivationStateManager<T> : IStateManager<T>
    where T : class, IEquatable<T>
{
    private IStateManager<T>? manager;

    public ActivationStateManager(IGrainLifecycle lifecycle, Func<IStateManager<T>> create)
    {
        // A distinct stage is essential: callbacks at SetupState can run in
        // parallel with Orleans' persistent-state hydration.
        lifecycle.Subscribe(GetType().FullName!, GrainLifecycleStage.SetupState + 1, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            manager = create();
            return Task.CompletedTask;
        });
    }

    private IStateManager<T> Manager => manager
        ?? throw new InvalidOperationException("State is not initialized. Access it after persistent state hydration, starting in OnActivateAsync.");

    public T State => Manager.State;

    public Task ReadAsync() => Manager.ReadAsync();

    public Task WriteAsync(T newState) => Manager.WriteAsync(newState);

    public Task ClearAsync() => Manager.ClearAsync();
}
