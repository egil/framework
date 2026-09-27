using System.Text.Json;
using System.Collections.Concurrent;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class OutboxStreamContextTests
{
    private const string Key = "egil.orleans.messaging.outbox";

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "outer")]
    public async Task Publication_restores_previous_entry_and_preserves_unrelated_context(bool hadPrevious, string? previous)
    {
        RequestContext.Remove(Key);
        if (hadPrevious)
            RequestContext.Set(Key, previous!);
        RequestContext.Set("unrelated", "retained");
        var stream = Stream();
        object? observed = null;
        await stream.SubscribeAsync(new Observer(_ =>
        {
            observed = RequestContext.Get(Key);
            Assert.Equal("retained", RequestContext.Get("unrelated"));
            return Task.CompletedTask;
        }));

        await stream.PublishFromOutboxAsync("plain-event", Token(1));

        Assert.Equal("v1:" + JsonSerializer.Serialize(Token(1)), observed);
        Assert.Equal(hadPrevious, RequestContext.Keys.Contains(Key));
        Assert.Equal(previous, RequestContext.Get(Key));
        Assert.Equal("retained", RequestContext.Get("unrelated"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_publication_restores_the_callers_context(bool cancelled)
    {
        RequestContext.Set(Key, "outer");
        var stream = Stream();
        await stream.SubscribeAsync(new Observer(_ => cancelled
            ? Task.FromCanceled(new CancellationToken(canceled: true))
            : Task.FromException(new IOException("publish failed"))));

        await Assert.ThrowsAnyAsync<Exception>(() => stream.PublishFromOutboxAsync("event", Token(1)));

        Assert.Equal("outer", RequestContext.Get(Key));
    }

    [Fact]
    public async Task Overlapping_publications_keep_each_identity_across_suspension()
    {
        RequestContext.Remove(Key);
        var stream = Stream();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var identities = new ConcurrentDictionary<string, object?>();
        var entered = 0;
        await stream.SubscribeAsync(new Observer(async message =>
        {
            var initial = RequestContext.Get(Key);
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.SetResult();
            await release.Task;
            Assert.Equal(initial, RequestContext.Get(Key));
            Assert.True(identities.TryAdd(message, initial));
        }));
        var first = stream.PublishFromOutboxAsync("first", Token(1));
        var second = stream.PublishFromOutboxAsync("second", Token(2));
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Key, RequestContext.Keys);

        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal("v1:" + JsonSerializer.Serialize(Token(1)), identities["first"]);
        Assert.Equal("v1:" + JsonSerializer.Serialize(Token(2)), identities["second"]);
        Assert.DoesNotContain(Key, RequestContext.Keys);
    }

    [Fact]
    public async Task Receiver_masks_incoming_identity_for_nested_raw_publication_and_preserves_full_source()
    {
        RequestContext.Remove(Key);
        RequestContext.Set("unrelated", "retained");
        var incoming = Stream();
        var nested = new StreamManagerResumeTests.FakeStream<string>("provider", StreamId.Create("nested", "one"));
        StreamCursor? nestedCursor = null;
        StreamCursor? receivedCursor = null;
        var nestedManager = StreamManagerResumeTests.CreateManager(null, nested)
            .ConfigureExplicitSubscription<string>("provider", nested.StreamId, (_, cursor) =>
            {
                nestedCursor = cursor;
                Assert.DoesNotContain(Key, RequestContext.Keys);
                return ValueTask.CompletedTask;
            }, options => options.Trace = MessageTraceOptions.None);
        await nestedManager.EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);
        var manager = StreamManagerResumeTests.CreateManager(null, incoming)
            .ConfigureExplicitSubscription<string>("provider", incoming.StreamId, async ValueTask (_, cursor) =>
            {
                receivedCursor = cursor;
                Assert.DoesNotContain(Key, RequestContext.Keys);
                await Task.Yield();
                await nested.OnNextAsync("nested");
                Assert.Equal("retained", RequestContext.Get("unrelated"));
            }, options => options.Trace = MessageTraceOptions.None);
        await manager.EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        await incoming.PublishFromOutboxAsync("incoming", Token(1));

        Assert.NotNull(receivedCursor);
        Assert.Equal(Token(1), receivedCursor.OutboxToken);
        Assert.Equal(incoming.StreamId, receivedCursor.StreamId);
        Assert.Null(receivedCursor.Token);
        Assert.NotNull(nestedCursor);
        Assert.Null(nestedCursor.OutboxToken);
        Assert.DoesNotContain(Key, RequestContext.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    [InlineData("v2:{}")]
    [InlineData("v1:null")]
    [InlineData("v1:{}")]
    [InlineData("v1:{")]
    [InlineData("v1:{\"SequenceNumber\":0,\"Sender\":{\"Type\":\"sender\",\"Key\":\"one\"},\"Timestamp\":\"1970-01-01T00:00:00Z\",\"Epoch\":\"1970-01-01T00:00:00Z\"}")]
    public async Task Malformed_present_metadata_faults_the_observer_before_application_code(object? value)
    {
        var stream = Stream();
        var calls = 0;
        var errors = 0;
        var manager = StreamManagerResumeTests.CreateManager(null, stream)
            .ConfigureExplicitSubscription<string>("provider", stream.StreamId, (_, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            }, options => options.OnError = (_, _) => errors++);
        await manager.EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);
        RequestContext.Set(Key, value!);

        await Assert.ThrowsAnyAsync<Exception>(() => stream.OnNextAsync("corrupt"));

        Assert.Equal(0, calls);
        Assert.Equal(0, errors);
        Assert.Equal(value, RequestContext.Get(Key));
    }

    private static StreamManagerResumeTests.FakeStream<string> Stream() => new("provider", StreamId.Create("orders", "one"));
    private static OutboxSequenceToken Token(long sequence) => new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private sealed class Observer(Func<string, Task> receive) : IAsyncObserver<string>
    {
        public Task OnNextAsync(string item, StreamSequenceToken? token = null) => receive(item);
        public Task OnCompletedAsync() => Task.CompletedTask;
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    }
}
