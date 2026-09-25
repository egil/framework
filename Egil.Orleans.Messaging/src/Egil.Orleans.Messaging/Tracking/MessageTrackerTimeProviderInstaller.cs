using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Applies <see cref="MessageTrackerOptions"/> to the <see cref="MessageTrackerClock"/>
/// fallback before grains activate and puts the previous value back when the silo stops.
/// </summary>
internal sealed class MessageTrackerTimeProviderInstaller(
    IOptions<MessageTrackerOptions> options,
    IServiceProvider services)
    : ILifecycleParticipant<ISiloLifecycle>, ILifecycleObserver
{
    private TimeProvider? previous;

    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(nameof(MessageTrackerTimeProviderInstaller), ServiceLifecycleStage.RuntimeInitialize, this);

    public Task OnStart(CancellationToken cancellationToken)
    {
        var timeProvider = options.Value.TimeProvider
            ?? services.GetService<TimeProvider>()
            ?? TimeProvider.System;
        previous = MessageTrackerClock.SiloDefault;
        MessageTrackerClock.Install(timeProvider);
        return Task.CompletedTask;
    }

    public Task OnStop(CancellationToken cancellationToken)
    {
        MessageTrackerClock.Install(previous);
        previous = null;
        return Task.CompletedTask;
    }
}
