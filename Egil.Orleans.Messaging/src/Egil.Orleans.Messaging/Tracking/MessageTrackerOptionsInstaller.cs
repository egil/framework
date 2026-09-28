using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Applies <see cref="MessageTrackerOptions"/> before grains activate and withdraws
/// this silo's settings when it stops.
/// </summary>
internal sealed class MessageTrackerOptionsInstaller(
    IOptions<MessageTrackerOptions> options,
    IServiceProvider services)
    : ILifecycleParticipant<ISiloLifecycle>, ILifecycleObserver
{
    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(nameof(MessageTrackerOptionsInstaller), ServiceLifecycleStage.RuntimeInitialize, this);

    public Task OnStart(CancellationToken cancellationToken)
    {
        var timeProvider = options.Value.TimeProvider
            ?? services.GetService<TimeProvider>()
            ?? TimeProvider.System;
        MessageTrackerDefaults.Install(this, MessageTrackerSettings.FromOptions(options.Value, timeProvider));
        return Task.CompletedTask;
    }

    public Task OnStop(CancellationToken cancellationToken)
    {
        MessageTrackerDefaults.Uninstall(this);
        return Task.CompletedTask;
    }
}
