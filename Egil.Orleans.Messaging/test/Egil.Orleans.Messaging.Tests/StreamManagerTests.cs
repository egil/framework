using Egil.Orleans.Testing;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests;

public sealed class StreamManagerTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task AddSubscription_with_ValueTask_handler_delivers_stream_messages()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(StreamManagerTestNamespaces.ValueTask, grainKey);

        await stream.OnNextAsync("value-task");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("value-task", (await grain.GetStateAsync()).ValueTaskValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddSubscription_with_Task_handler_delivers_stream_messages()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(StreamManagerTestNamespaces.Task, grainKey);

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
        var stream = fixture.GetStream<string>(StreamManagerTestNamespaces.Failure, grainKey);

        await stream.OnNextAsync("fail");
        await stream.OnNextAsync("after-failure");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("after-failure", (await grain.GetStateAsync()).FailureValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_uses_tracker_snapshot_when_resume_token_is_configured()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.SeedResumeCursorAsync(7);
        await grain.DeactivateAsync();

        grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(grainKey);
        await grain.EnsureActiveAsync();
        var stream = fixture.GetStream<string>(StreamManagerTestNamespaces.Resume, grainKey);

        await stream.OnNextAsync("resumed");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("resumed", (await grain.GetStateAsync()).ResumeValue),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_rejects_duplicate_subscription_initialization()
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(Guid.NewGuid());
        await grain.EnsureActiveAsync();

        var exceptionType = await grain.SubscribeAgainAsync();

        Assert.Equal(nameof(InvalidOperationException), exceptionType);
    }

    [Fact]
    public async Task AddSubscription_rejects_configuration_after_subscribe()
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerTestGrain>(Guid.NewGuid());
        await grain.EnsureActiveAsync();

        var exceptionType = await grain.AddSubscriptionAfterSubscribeAsync();

        Assert.Equal(nameof(InvalidOperationException), exceptionType);
    }
}

internal static class StreamManagerTestNamespaces
{
    public const string ValueTask = "stream-manager-valuetask";
    public const string Task = "stream-manager-task";
    public const string Failure = "stream-manager-failure";
    public const string Resume = "stream-manager-resume";
}

public interface IStreamManagerTestGrain : IGrainWithGuidKey
{
    Task EnsureActiveAsync();

    Task SeedResumeCursorAsync(long sequenceNumber);

    Task DeactivateAsync();

    Task<string?> SubscribeAgainAsync();

    Task<string?> AddSubscriptionAfterSubscribeAsync();

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
}

public sealed class StreamManagerTestGrain(
    [PersistentState("state", "Default")] IPersistentState<StreamManagerTestState> state)
    : Grain, IStreamManagerTestGrain
{
    private StreamManager? streamManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        streamManager = this.RegisterStreamManager(state.State.Tracker)
            .AddSubscription<string>(StreamManagerTestNamespaces.ValueTask, HandleValueTaskAsync)
            .AddSubscription<string>(StreamManagerTestNamespaces.Task, HandleTaskAsync)
            .AddSubscription<string>(StreamManagerTestNamespaces.Failure, HandleFailureAsync)
            .AddSubscription<string>(StreamManagerTestNamespaces.Resume, HandleResumeAsync);

        await streamManager.SubscribeAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public async Task SeedResumeCursorAsync(long sequenceNumber)
    {
        var cursor = new StreamCursor(
            StreamId.Create(StreamManagerTestNamespaces.Resume, this.GetPrimaryKey()),
            new EventSequenceToken(sequenceNumber));

        state.State.Tracker.ProcessMessage(cursor, out var next);
        state.State.Tracker = next;
        await state.WriteStateAsync();
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<StreamManagerTestState> GetStateAsync() => Task.FromResult(state.State);

    public async Task<string?> SubscribeAgainAsync()
    {
        try
        {
            await streamManager!.SubscribeAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    public Task<string?> AddSubscriptionAfterSubscribeAsync()
    {
        try
        {
            streamManager!.AddSubscription<string>(
                "not-registered",
                static (_, _) => ValueTask.CompletedTask);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>(ex.GetType().Name);
        }
    }

    private async ValueTask HandleValueTaskAsync(string item, StreamSequenceToken? token)
    {
        TrackStreamCursor(StreamManagerTestNamespaces.ValueTask, token);
        state.State.ValueTaskValue = item;
        await state.WriteStateAsync();
    }

    private async Task HandleTaskAsync(string item, StreamSequenceToken? token)
    {
        TrackStreamCursor(StreamManagerTestNamespaces.Task, token);
        state.State.TaskValue = item;
        await state.WriteStateAsync();
    }

    private async ValueTask HandleFailureAsync(string item, StreamSequenceToken? token)
    {
        if (item == "fail")
        {
            throw new InvalidOperationException("Handler failed.");
        }

        TrackStreamCursor(StreamManagerTestNamespaces.Failure, token);
        state.State.FailureValue = item;
        await state.WriteStateAsync();
    }

    private async ValueTask HandleResumeAsync(string item, StreamSequenceToken? token)
    {
        TrackStreamCursor(StreamManagerTestNamespaces.Resume, token);
        state.State.ResumeValue = item;
        await state.WriteStateAsync();
    }

    private void TrackStreamCursor(string streamNamespace, StreamSequenceToken? token)
    {
        var cursor = new StreamCursor(StreamId.Create(streamNamespace, this.GetPrimaryKey()), token);
        if (state.State.Tracker.ProcessMessage(cursor, out var next))
        {
            state.State.Tracker = next;
        }

    }
}
