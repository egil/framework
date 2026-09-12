namespace Egil.Orleans.Messaging.Tests.State;

public sealed class OutboxRecoveryProofTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Competing_append_must_not_be_confirmed_as_the_attempted_write(int competingTimestampOffsetTicks)
    {
        var timestamp = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var initial = Outbox<string>.Create().Add("already pending", timestamp);
        var attempted = initial.Add("attempted message", timestamp.AddSeconds(1));
        var competing = initial.Add("competing message", timestamp.AddSeconds(1).AddTicks(competingTimestampOffsetTicks));
        var storage = new AmbiguousOutboxStorage(initial, competing);
        var manager = new DefaultStateManager<Outbox<string>>(storage, Outbox<string>.Create);

        var error = await Record.ExceptionAsync(() => manager.WriteAsync(attempted));

        Assert.DoesNotContain(manager.State, item => item == "attempted message");
        Assert.IsType<TimeoutException>(error);
    }

    [Fact]
    public async Task Saved_snapshot_with_lost_response_is_confirmed_after_deserialization()
    {
        var initial = Outbox<string>.Create();
        var attempted = initial.Add("saved message", DateTimeOffset.UnixEpoch);
        var durable = System.Text.Json.JsonSerializer.Deserialize<Outbox<string>>(
            System.Text.Json.JsonSerializer.Serialize(attempted))!;
        var storage = new AmbiguousOutboxStorage(initial, durable);
        var manager = new DefaultStateManager<Outbox<string>>(storage, Outbox<string>.Create);

        await manager.WriteAsync(attempted);

        Assert.Equal("saved message", Assert.Single(manager.State));
        Assert.Equal(attempted, manager.State);
    }

    private sealed class AmbiguousOutboxStorage(Outbox<string> initial, Outbox<string> durable) : IPersistentState<Outbox<string>>
    {
        public Outbox<string> State { get; set; } = initial;
        public string Etag { get; set; } = "initial";
        public bool RecordExists => true;

        public Task ReadStateAsync()
        {
            State = durable;
            Etag = "competing";
            return Task.CompletedTask;
        }

        // An unknown-outcome failure permits a competing write to be visible on recovery.
        public Task WriteStateAsync() => Task.FromException(new TimeoutException("Unknown write outcome"));
        public Task ClearStateAsync() => throw new NotSupportedException();
        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();
        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }
}
