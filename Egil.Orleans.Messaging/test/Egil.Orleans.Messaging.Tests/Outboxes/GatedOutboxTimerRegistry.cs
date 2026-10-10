using Orleans.Timers;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

internal sealed class GatedOutboxTimerRegistry(ITimerRegistry timers) : ITimerRegistry
{
    public static void Install(IServiceCollection services)
    {
        var registration = services.Single(service => service.ServiceType == typeof(ITimerRegistry));
        services.Remove(registration);
        services.AddSingleton<ITimerRegistry>(provider => new GatedOutboxTimerRegistry(
            (ITimerRegistry)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!)));
    }

    [Obsolete("Use RegisterGrainTimer instead.")]
    public IDisposable RegisterTimer(IGrainContext grainContext, Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        timers.RegisterTimer(grainContext, callback, state, dueTime, period);

    public IGrainTimer RegisterGrainTimer<TState>(
        IGrainContext grainContext,
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        if (grainContext.GrainInstance is not OutboxDrainRequestGrain grain)
        {
            return timers.RegisterGrainTimer(grainContext, callback, state, options);
        }

        var gate = grain.Gate;
        // This test grain registers only the processor's two timers: dispatch
        // repeats at RetryDelay, while acknowledgement is a single-shot timer.
        // Decorate that runtime boundary without replacing either callback.
        var acknowledgementTimer = options.Period == Timeout.InfiniteTimeSpan;
        var timer = timers.RegisterGrainTimer(grainContext, async (timerState, cancellationToken) =>
        {
            var requestedDispatchTick = !acknowledgementTimer && gate.DispatchTickRequested.Task.IsCompleted;
            if (acknowledgementTimer && grain.HoldAcknowledgementTimer)
            {
                // Hold before the processor acquires its gate. The real Orleans
                // dispatch timer can then hit the pending-batch defer branch,
                // rather than merely wait behind an active acknowledgement.
                gate.AcknowledgementTimerStarted.TrySetResult();
                await gate.AllowAcknowledgement.Task.WaitAsync(cancellationToken);
            }

            await callback(timerState, cancellationToken);
            if (acknowledgementTimer)
            {
                gate.AcknowledgementCallbackCompleted.TrySetResult();
            }
            else if (requestedDispatchTick)
            {
                gate.RequestedDispatchCompleted.TrySetResult();
            }
        }, state, options);

        if (!acknowledgementTimer)
        {
            gate.DispatchTimer = new GatedOutboxDispatchTimer(timer, options);
            return gate.DispatchTimer;
        }

        return timer;
    }
}

internal sealed class GatedOutboxDispatchTimer(IGrainTimer timer, GrainTimerCreationOptions options) : IGrainTimer
{
    private TimeSpan dueTime = options.DueTime;
    private TimeSpan period = options.Period;
    private bool paused;

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        this.dueTime = dueTime;
        this.period = period;
        timer.Change(paused ? Timeout.InfiniteTimeSpan : dueTime, period);
    }

    public void Pause()
    {
        // Retain schedule changes while suppressing their runtime wakeup. This
        // lets a queued stale acknowledgement finish before the follow-up timer
        // fires, without changing the processor or invoking its private methods.
        paused = true;
        timer.Change(Timeout.InfiniteTimeSpan, period);
    }

    public void Resume()
    {
        paused = false;
        timer.Change(dueTime, period);
    }

    public void Dispose() => timer.Dispose();
}
