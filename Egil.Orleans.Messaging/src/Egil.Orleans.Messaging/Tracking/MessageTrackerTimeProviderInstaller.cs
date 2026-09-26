using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Applies <see cref="MessageTrackerOptions"/> to the <see cref="MessageTrackerClock"/>
/// fallback before grains activate and withdraws this silo's clock when it stops.
/// </summary>
internal sealed class MessageTrackerTimeProviderInstaller(
    IOptions<MessageTrackerOptions> options,
    IServiceProvider services)
    : ILifecycleParticipant<ISiloLifecycle>, ILifecycleObserver
{
    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(nameof(MessageTrackerTimeProviderInstaller), ServiceLifecycleStage.RuntimeInitialize, this);

    public Task OnStart(CancellationToken cancellationToken)
    {
        var timeProvider = options.Value.TimeProvider
            ?? services.GetService<TimeProvider>()
            ?? TimeProvider.System;
        MessageTrackerClock.Install(this, timeProvider);
        return Task.CompletedTask;
    }

    public Task OnStop(CancellationToken cancellationToken)
    {
        MessageTrackerClock.Uninstall(this);
        return Task.CompletedTask;
    }
}
