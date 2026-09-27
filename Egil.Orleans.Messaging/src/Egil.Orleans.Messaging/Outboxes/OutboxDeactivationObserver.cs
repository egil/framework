using Orleans.Metadata;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Installs the shutdown hook before Orleans starts the lifecycle, including
/// for processors which the grain creates later in OnActivateAsync.
/// </summary>
internal sealed class OutboxDeactivationObserver(IGrainContext context) : ILifecycleObserver
{
    public static void Install(IGrainContext context)
    {
        if (context.GetComponent<OutboxDeactivationObserver>() is not null)
        {
            return;
        }

        var observer = new OutboxDeactivationObserver(context);
        context.ObservableLifecycle.Subscribe(nameof(OutboxDeactivationObserver), GrainLifecycleStage.Last, observer);
        context.SetComponent(observer);
    }

    public Task OnStart(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnStop(CancellationToken cancellationToken) =>
        context.GetComponent<IOutboxComponent>()?.OnDeactivateAsync(cancellationToken) ?? Task.CompletedTask;
}

internal sealed class OutboxGrainContextConfigurator(GrainClassMap grainClasses)
    : IConfigureGrainContextProvider, IConfigureGrainContext
{
    public bool TryGetConfigurator(GrainType grainType, GrainProperties properties,
        [NotNullWhen(true)] out IConfigureGrainContext? configurator)
    {
        configurator = grainClasses.TryGetGrainClass(grainType, out var grainClass)
            && typeof(IOutboxGrain).IsAssignableFrom(grainClass) ? this : null;
        return configurator is not null;
    }

    public void Configure(IGrainContext context) => OutboxDeactivationObserver.Install(context);
}
