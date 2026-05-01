using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Streams;
using Orleans.Timers;

namespace Egil.Orleans.Testing.Tests;

internal static class ActivityFeatureTestConstants
{
    public const string StreamProviderName = "StreamProvider";
    public const string ExplicitStreamNamespace = "explicit-activity";
    public const string ImplicitStreamNamespace = "implicit-activity";
}

public sealed class ActivityFeatureState
{
    public string? LastValue { get; set; }

    public string? PendingValue { get; set; }
}

public interface IOneWayActivityGrain : IGrainWithStringKey
{
    [OneWay]
    Task NotifyAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class OneWayActivityGrain([PersistentState("state", "Default")] IPersistentState<ActivityFeatureState> state)
    : Grain, IOneWayActivityGrain
{
    public async Task NotifyAsync(string value)
    {
        state.State.LastValue = value;
        await state.WriteStateAsync();
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);
}

public interface IExplicitStreamActivityGrain : IGrainWithGuidKey
{
    Task SubscribeAsync();

    Task<string?> GetLastValueAsync();
}

public sealed class ExplicitStreamActivityGrain(
    [PersistentState("state", "Default")] IPersistentState<ActivityFeatureState> state,
    IGrainContext grainContext)
    : Grain, IExplicitStreamActivityGrain, IAsyncObserver<string>
{
    private StreamSubscriptionHandle<string>? subscription;

    public async Task SubscribeAsync()
    {
        if (subscription is not null)
        {
            return;
        }

        var streamProvider = grainContext.ActivationServices.GetRequiredKeyedService<IStreamProvider>(ActivityFeatureTestConstants.StreamProviderName);
        var stream = streamProvider.GetStream<string>(StreamId.Create(ActivityFeatureTestConstants.ExplicitStreamNamespace, this.GetPrimaryKey()));
        subscription = await stream.SubscribeAsync(this);
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    public async Task OnNextAsync(string item, StreamSequenceToken? token = null)
    {
        state.State.LastValue = item;
        await state.WriteStateAsync();
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex) => Task.FromException(ex);
}

public interface IImplicitStreamActivityGrain : IGrainWithGuidKey
{
    Task<string?> GetLastValueAsync();
}

[ImplicitStreamSubscription(ActivityFeatureTestConstants.ImplicitStreamNamespace)]
public sealed class ImplicitStreamActivityGrain(
    [PersistentState("state", "Default")] IPersistentState<ActivityFeatureState> state,
    IGrainContext grainContext)
    : Grain, IImplicitStreamActivityGrain, IAsyncObserver<string>
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var streamProvider = grainContext.ActivationServices.GetRequiredKeyedService<IStreamProvider>(ActivityFeatureTestConstants.StreamProviderName);
        var stream = streamProvider.GetStream<string>(StreamId.Create(ActivityFeatureTestConstants.ImplicitStreamNamespace, this.GetPrimaryKey()));
        await stream.SubscribeAsync(this);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    public async Task OnNextAsync(string item, StreamSequenceToken? token = null)
    {
        state.State.LastValue = item;
        await state.WriteStateAsync();
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex) => Task.FromException(ex);
}

public interface IReminderActivityGrain : IGrainWithStringKey
{
    Task StartReminderAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class ReminderActivityGrain(
    [PersistentState("state", "Default")] IPersistentState<ActivityFeatureState> state,
    IReminderRegistry reminderRegistry,
    IGrainContext grainContext)
    : Grain, IReminderActivityGrain, IRemindable
{
    private const string ReminderName = "activity-reminder";
    private IGrainReminder? reminder;

    public async Task StartReminderAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();
        reminder = await reminderRegistry.RegisterOrUpdateReminder(grainContext.GrainId, ReminderName, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();

        if (reminder is not null)
        {
            await reminderRegistry.UnregisterReminder(grainContext.GrainId, reminder);
            reminder = null;
        }
    }
}

public interface ITimerActivityGrain : IGrainWithStringKey
{
    Task StartTimerAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class TimerActivityGrain(
    [PersistentState("state", "Default")] IPersistentState<ActivityFeatureState> state,
    ITimerRegistry timerRegistry,
    IGrainContext grainContext)
    : Grain, ITimerActivityGrain
{
    private IGrainTimer? timer;

    public async Task StartTimerAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();

        timer?.Dispose();
        timer = timerRegistry.RegisterGrainTimer(
            grainContext,
            static (grain, ct) => grain.OnTimerTickAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(1),
                Period = Timeout.InfiniteTimeSpan,
            });
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    private async Task OnTimerTickAsync(CancellationToken cancellationToken)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();
        timer?.Dispose();
        timer = null;
    }
}
