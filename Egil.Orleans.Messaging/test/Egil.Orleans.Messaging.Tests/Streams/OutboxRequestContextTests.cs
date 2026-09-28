using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class OutboxRequestContextTests
{
    private const string Key = "egil.orleans.messaging.outbox";

    public static TheoryData<object?> InvalidContextValues => new()
    {
        (object?)null,
        42,
        // Old transport metadata must fail explicitly rather than silently lose deduplication on upgrade.
        "v1:{\"SequenceNumber\":1,\"Sender\":{\"Type\":\"sender\",\"Key\":\"one\"},\"Timestamp\":\"1970-01-01T00:00:00Z\",\"Epoch\":\"1970-01-01T00:00:00Z\"}",
        new OutboxSequenceToken(),
        Token(0),
        Token(-1),
        Token(1) with { Sender = default }
    };

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "outer")]
    public async Task Scope_carries_a_typed_token_across_await_and_restores_only_its_own_entry(bool hadPrevious, string? previous)
    {
        RequestContext.Remove(Key);
        if (hadPrevious)
            RequestContext.Set(Key, previous!);
        RequestContext.Set("unrelated", "before");
        var token = Token(1) with { TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" };

        using (RequestContext.AttachOutboxToken(token))
        {
            await Task.Yield();
            Assert.Equal(token, Assert.IsType<OutboxSequenceToken>(RequestContext.Get(Key)));
            var received = RequestContext.GetOutboxToken();
            Assert.Equal(token, received);
            Assert.Equal(token.TraceParent, received!.TraceParent);
            Assert.Equal(received, RequestContext.GetOutboxToken());
            RequestContext.Set("unrelated", "after");
        }

        Assert.Equal(hadPrevious, RequestContext.Keys.Contains(Key));
        Assert.Equal(previous, RequestContext.Get(Key));
        Assert.Equal("after", RequestContext.Get("unrelated"));
    }

    [Fact]
    public void Missing_identity_returns_null_without_creating_an_entry()
    {
        RequestContext.Remove(Key);

        Assert.Null(RequestContext.GetOutboxToken());
        Assert.Null(RequestContext.DetachOutboxToken());

        Assert.DoesNotContain(Key, RequestContext.Keys);
    }

    [Fact]
    public async Task Detaching_returns_the_token_and_leaves_the_flow_without_identity_across_await()
    {
        RequestContext.Remove(Key);
        RequestContext.Set("unrelated", "retained");
        var token = Token(1);
        using var incoming = RequestContext.AttachOutboxToken(token);

        Assert.Equal(token, RequestContext.DetachOutboxToken());
        await Task.Yield();

        Assert.DoesNotContain(Key, RequestContext.Keys);
        Assert.Null(RequestContext.GetOutboxToken());
        Assert.Null(RequestContext.DetachOutboxToken());
        Assert.Equal("retained", RequestContext.Get("unrelated"));
        using (RequestContext.AttachOutboxToken(Token(2)))
            Assert.Equal(Token(2), RequestContext.GetOutboxToken());
        Assert.Null(RequestContext.GetOutboxToken());
    }

    [Fact]
    public void Nested_scopes_restore_outer_identity_and_repeated_disposal_has_no_effect()
    {
        RequestContext.Remove(Key);
        using var outer = RequestContext.AttachOutboxToken(Token(1));
        using var inner = RequestContext.AttachOutboxToken(Token(2));
        Assert.Equal(Token(2), RequestContext.GetOutboxToken());

        inner.Dispose();
        Assert.Equal(Token(1), RequestContext.GetOutboxToken());
        outer.Dispose();
        inner.Dispose();
        outer.Dispose();

        Assert.Null(RequestContext.GetOutboxToken());
    }

    [Fact]
    public async Task Overlapping_async_scopes_keep_their_own_identity()
    {
        RequestContext.Remove(Key);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var first = ReadAfterReleaseAsync(Token(1));
        var second = ReadAfterReleaseAsync(Token(2));
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Null(RequestContext.GetOutboxToken());

        release.SetResult();
        var observed = await Task.WhenAll(first, second);

        Assert.Collection(observed, token => Assert.Equal(Token(1), token), token => Assert.Equal(Token(2), token));
        Assert.Null(RequestContext.GetOutboxToken());

        async Task<OutboxSequenceToken?> ReadAfterReleaseAsync(OutboxSequenceToken token)
        {
            using var scope = RequestContext.AttachOutboxToken(token);
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.SetResult();
            await release.Task;
            return RequestContext.GetOutboxToken();
        }
    }

    [Theory]
    [MemberData(nameof(InvalidContextValues))]
    public void Invalid_present_metadata_throws_without_consuming_the_entry(object? value)
    {
        RequestContext.Set(Key, value!);

        Assert.Throws<InvalidOperationException>(() => RequestContext.GetOutboxToken());
        Assert.Throws<InvalidOperationException>(() => RequestContext.DetachOutboxToken());

        Assert.Contains(Key, RequestContext.Keys);
        Assert.Equal(value, RequestContext.Get(Key));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public void Invalid_attachment_leaves_the_previous_entry_untouched(long sequence, bool defaultSender)
    {
        RequestContext.Set(Key, "outer");
        var token = Token(sequence);
        if (defaultSender)
            token = token with { Sender = default };

        Assert.Throws<ArgumentException>(() => RequestContext.AttachOutboxToken(token));

        Assert.Equal("outer", RequestContext.Get(Key));
    }

    [Fact]
    public void Null_attachment_leaves_the_previous_entry_untouched()
    {
        RequestContext.Set(Key, "outer");

        Assert.Throws<ArgumentNullException>(() => RequestContext.AttachOutboxToken(null!));

        Assert.Equal("outer", RequestContext.Get(Key));
    }

    private static OutboxSequenceToken Token(long sequence) =>
        new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
