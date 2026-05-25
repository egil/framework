using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task ConfigureExplicitSubscription_with_ValueTask_handler_delivers_stream_messages()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Explicit,
            StreamManagerTestNamespaces.ValueTask,
            grainKey);

        await stream.OnNextAsync("value-task");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("value-task", (await grain.GetStateAsync()).ValueTaskValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Implicit_subscription_activates_grain_and_delivers_stream_message()
    {
        var grainKey = Guid.NewGuid();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Implicit,
            StreamManagerTestNamespaces.Implicit,
            grainKey);

        await stream.OnNextAsync("implicit");

        var grain = fixture.GrainFactory.GetGrain<IImplicitStreamManagerTestGrain>(grainKey);
        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("implicit", await grain.GetValueAsync()),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Implicit_subscription_does_not_duplicate_after_reactivation()
    {
        var grainKey = Guid.NewGuid();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Implicit,
            StreamManagerTestNamespaces.Implicit,
            grainKey);

        await stream.OnNextAsync("first");

        var grain = fixture.GrainFactory.GetGrain<IImplicitStreamManagerTestGrain>(grainKey);
        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal(1, await grain.GetDeliveryCountAsync()),
            ct: TestContext.Current.CancellationToken);
        await grain.DeactivateAsync();

        await stream.OnNextAsync("second");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal(2, await grain.GetDeliveryCountAsync()),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Implicit_subscription_has_no_explicit_subscription_handles()
    {
        var grainKey = Guid.NewGuid();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Implicit,
            StreamManagerTestNamespaces.Implicit,
            grainKey);

        await stream.OnNextAsync("implicit");

        var grain = fixture.GrainFactory.GetGrain<IImplicitStreamManagerTestGrain>(grainKey);
        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("implicit", await grain.GetValueAsync()),
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(await stream.GetAllSubscriptionHandles());
    }

    [Fact]
    public async Task Explicit_subscription_does_not_duplicate_after_reactivation()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        await grain.DeactivateAsync();

        grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Explicit,
            StreamManagerTestNamespaces.ValueTask,
            grainKey);

        await stream.OnNextAsync("after-reactivation");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal(1, (await grain.GetStateAsync()).ValueTaskDeliveryCount),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfigureExplicitSubscription_with_Task_handler_delivers_stream_messages()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Explicit,
            StreamManagerTestNamespaces.Task,
            grainKey);

        await stream.OnNextAsync("task");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("task", (await grain.GetStateAsync()).TaskValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Default_error_handler_does_not_break_subscription_when_handler_throws()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Explicit,
            StreamManagerTestNamespaces.Failure,
            grainKey);

        await stream.OnNextAsync("fail");
        await stream.OnNextAsync("after-failure");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("after-failure", (await grain.GetStateAsync()).FailureValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_is_idempotent()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(
            StreamManagerTestProviderNames.Explicit,
            StreamManagerTestNamespaces.ValueTask,
            grainKey);

        var exceptionType = await grain.EnsureExplicitSubscriptionsAgainAsync();

        Assert.Null(exceptionType);

        await stream.OnNextAsync("idempotent");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal(1, (await grain.GetStateAsync()).ValueTaskDeliveryCount),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfigureExplicitSubscription_rejects_configuration_after_resume()
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(Guid.NewGuid());
        await grain.EnsureActiveAsync();

        var exceptionType = await grain.ConfigureExplicitSubscriptionAfterResumeAsync();

        Assert.Equal(nameof(InvalidOperationException), exceptionType);
    }
}

internal static class StreamManagerTestNamespaces
{
    public const string ValueTask = "stream-manager-valuetask";
    public const string Task = "stream-manager-task";
    public const string Failure = "stream-manager-failure";
    public const string Resume = "stream-manager-resume";
    public const string Implicit = "stream-manager-implicit";
}

internal static class StreamManagerTestProviderNames
{
    public const string Explicit = "stream-manager-explicit-provider";
    public const string Implicit = "stream-manager-provider";
}

public interface IStreamManagerTestGrain : IGrainWithGuidKey
{
    Task EnsureActiveAsync();

    Task DeactivateAsync();

    Task<string?> EnsureExplicitSubscriptionsAgainAsync();

    Task<string?> ConfigureExplicitSubscriptionAfterResumeAsync();

    Task<StreamManagerTestState> GetStateAsync();
}

[GenerateSerializer]
public sealed class StreamManagerTestState
{
    [Id(0)]
    public MessageTracker Tracker { get; set; } = new();

    [Id(1)]
    public string? ValueTaskValue { get; set; }

    [Id(2)]
    public string? TaskValue { get; set; }

    [Id(3)]
    public string? FailureValue { get; set; }

    [Id(4)]
    public string? ResumeValue { get; set; }

    [Id(5)]
    public int ValueTaskDeliveryCount { get; set; }
}

public sealed class StreamManagerTestGrain(
    [PersistentState("state", "Default")] IPersistentState<StreamManagerTestState> state)
    : Grain, IStreamManagerTestGrain
{
    private StreamManager? streamManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        streamManager = this.RegisterStreamManager(state.State.Tracker)
            .ConfigureExplicitSubscription<string>(StreamManagerTestProviderNames.Explicit, StreamManagerTestNamespaces.ValueTask, HandleValueTaskAsync)
            .ConfigureExplicitSubscription<string>(StreamManagerTestProviderNames.Explicit, StreamManagerTestNamespaces.Task, HandleTaskAsync)
            .ConfigureExplicitSubscription<string>(StreamManagerTestProviderNames.Explicit, StreamManagerTestNamespaces.Failure, HandleFailureAsync)
            .ConfigureExplicitSubscription<string>(StreamManagerTestProviderNames.Explicit, StreamManagerTestNamespaces.Resume, HandleResumeAsync);

        await streamManager.EnsureExplicitSubscriptionsAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<StreamManagerTestState> GetStateAsync() => Task.FromResult(state.State);

    public async Task<string?> EnsureExplicitSubscriptionsAgainAsync()
    {
        try
        {
            await streamManager!.EnsureExplicitSubscriptionsAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    public Task<string?> ConfigureExplicitSubscriptionAfterResumeAsync()
    {
        try
        {
            streamManager!.ConfigureExplicitSubscription<string>(
                "not-registered",
                "not-registered",
                static (_, _) => ValueTask.CompletedTask);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>(ex.GetType().Name);
        }
    }

    private async ValueTask HandleValueTaskAsync(string item, StreamCursor cursor)
    {
        TrackStreamCursor(cursor);
        state.State.ValueTaskValue = item;
        state.State.ValueTaskDeliveryCount++;
        await state.WriteStateAsync();
    }

    private async Task HandleTaskAsync(string item, StreamCursor cursor)
    {
        TrackStreamCursor(cursor);
        state.State.TaskValue = item;
        await state.WriteStateAsync();
    }

    private async ValueTask HandleFailureAsync(string item, StreamCursor cursor)
    {
        if (item == "fail")
        {
            throw new InvalidOperationException("Handler failed.");
        }

        TrackStreamCursor(cursor);
        state.State.FailureValue = item;
        await state.WriteStateAsync();
    }

    private async ValueTask HandleResumeAsync(string item, StreamCursor cursor)
    {
        TrackStreamCursor(cursor);
        state.State.ResumeValue = item;
        await state.WriteStateAsync();
    }

    private void TrackStreamCursor(StreamCursor cursor)
    {
        if (state.State.Tracker.ProcessMessage(cursor, out var next))
        {
            state.State.Tracker = next;
        }

    }
}

public interface IImplicitStreamManagerTestGrain : IGrainWithGuidKey
{
    Task<string?> GetValueAsync();

    Task<int> GetDeliveryCountAsync();

    Task DeactivateAsync();
}

[ImplicitStreamSubscription(StreamManagerTestNamespaces.Implicit)]
public sealed class ImplicitStreamManagerTestGrain(
    [PersistentState("state", "Default")] IPersistentState<ImplicitStreamManagerTestState> state)
    : Grain, IImplicitStreamManagerTestGrain, IImplicitStreamGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        this.RegisterStreamManager()
            .ConfigureImplicitSubscription<string>(StreamManagerTestNamespaces.Implicit, HandleImplicitAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public Task<string?> GetValueAsync() => Task.FromResult(state.State.Value);

    public Task<int> GetDeliveryCountAsync() => Task.FromResult(state.State.DeliveryCount);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private async ValueTask HandleImplicitAsync(string item, StreamCursor cursor)
    {
        state.State.Tracker.ProcessMessage(cursor, out var tracker);
        state.State.Tracker = tracker;
        state.State.Value = item;
        state.State.DeliveryCount++;
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class ImplicitStreamManagerTestState
{
    [Id(0)]
    public MessageTracker Tracker { get; set; } = new();

    [Id(1)]
    public string? Value { get; set; }

    [Id(2)]
    public int DeliveryCount { get; set; }
}