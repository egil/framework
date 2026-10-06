using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class RegistrationTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task Duplicate_messaging_component_names_are_rejected_before_initialization()
    {
        await using var session = fixture.CreateSession();
        Assert.True(session.Manager.TryGetStateMachine("outbox", out var existing));
        Assert.Same(session.Outbox, existing);
        Assert.Throws<InvalidOperationException>(() => session.Manager.RegisterStateMachine("outbox", existing!));
        Assert.Throws<InvalidOperationException>(() => session.Manager.RegisterStateMachine("outbox", (IStateMachine)session.Tracker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_grain_composition_recovers_all_components_before_activation(bool compact)
    {
        var grain = fixture.NewPlainGrain();
        var token = new OutboxSequenceToken(1, GrainId.Create("sender", "plain"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        await grain.ReceiveAsync(token, "plain", compact);
        var saved = await grain.ReadAsync();
        Assert.True(await grain.UsesCanonicalOutboxAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.RegisterIncompatibleOutboxAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.RegisterLateOutboxAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.RegisterLateTrackerAsync());
        await grain.DeactivateAsync();
        var recovered = await grain.ReadAsync();
        Assert.NotEqual(saved.Activation, recovered.Activation);
        Assert.Equal(saved.Business, recovered.Business);
        Assert.Equal(saved.Tracker, recovered.Tracker);
        Assert.Equal(saved.Outbox, recovered.Outbox);
        await grain.ReceiveAsync(token, "duplicate", false);
        Assert.Equal(saved.Business, (await grain.ReadAsync()).Business);
    }

    [Fact]
    public async Task Arbitrary_names_and_payload_types_recover_independently_in_one_commit()
    {
        var grain = fixture.NewNamedGrain();
        await grain.WriteAsync();
        await grain.DeactivateAsync();
        Assert.Equal("first/second/42", await grain.ReadAsync());
    }
}

public interface INamedComponentsGrain : IGrainWithGuidKey
{
    Task WriteAsync();
    Task DeactivateAsync();
    Task<string> ReadAsync();
}

public sealed class NamedComponentsGrain(
    [FromKeyedServices("first")] IDurableOutbox<string> first,
    [FromKeyedServices("second")] IDurableOutbox<string> second,
    [FromKeyedServices("numbers")] IDurableOutbox<int> numbers,
    [FromKeyedServices("receiver")] IDurableMessageTracker tracker) : DurableGrain, INamedComponentsGrain
{
    public async Task WriteAsync()
    {
        first.Add("first");
        second.Add("second");
        numbers.Add(42);
        tracker.Evict(DateTimeOffset.MinValue);
        await WriteStateAsync();
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<string> ReadAsync() => Task.FromResult($"{first[0]}/{second[0]}/{numbers[0]}");
}
