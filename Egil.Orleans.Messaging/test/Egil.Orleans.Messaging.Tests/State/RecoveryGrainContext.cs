namespace Egil.Orleans.Messaging.Tests.State;

internal sealed class RecoveryGrainContext(IServiceProvider services) : IGrainContext
{
    public IServiceProvider ActivationServices { get; } = services;

    public GrainReference GrainReference => throw new NotSupportedException();
    public GrainId GrainId => throw new NotSupportedException();
    public object? GrainInstance { get; set; } = new object();
    public ActivationId ActivationId => throw new NotSupportedException();
    public GrainAddress Address => throw new NotSupportedException();
    public IGrainLifecycle ObservableLifecycle => throw new NotSupportedException();
    public IWorkItemScheduler Scheduler => throw new NotSupportedException();
    public Task Deactivated => throw new NotSupportedException();

    public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Action<DeactivationReason, CancellationToken>? OnDeactivate { get; set; }
    public int Deactivations { get; private set; }
    public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default)
    {
        Deactivations++;
        OnDeactivate?.Invoke(deactivationReason, cancellationToken);
    }

    public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void ReceiveMessage(object message) => throw new NotSupportedException();

    public void Rehydrate(IRehydrationContext context) => throw new NotSupportedException();

    public void SetComponent<TComponent>(TComponent? value) where TComponent : class
        => throw new NotSupportedException();

    public TComponent? GetComponent<TComponent>() where TComponent : class
        => throw new NotSupportedException();

    public TTarget GetTarget<TTarget>() where TTarget : class => throw new NotSupportedException();

    public object GetComponent(Type componentType) => throw new NotSupportedException();

    public object GetTarget() => throw new NotSupportedException();

    public bool Equals(IGrainContext? other) => ReferenceEquals(this, other);
}
