using Egil.Orleans.Messaging.Tests.Fakes;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class MultiStreamPostmanTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData("fixed")]
    [InlineData("grouped-fixed")]
    [InlineData("selector")]
    [InlineData("grouped-selector")]
    public async Task One_registration_publishes_to_every_destination_including_repeated_ids(string mode)
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, target);

        var result = await source.PublishAsync(target, "submitted");

        Assert.Equal((0, 1, null), result);
        var firstDeliveries = fixture.FanOutStreams.Read(StreamId.Create("first", target));
        Assert.Equal(2, firstDeliveries.Length);
        var first = firstDeliveries[0];
        Assert.Equal(first, firstDeliveries[1]);
        var second = Assert.Single(fixture.FanOutStreams.Read(StreamId.Create("second", target)));
        Assert.Equal(new MultiStreamMessage(target, "submitted"), first.Message);
        Assert.Equal(first.Message, second.Message);
        Assert.NotNull(first.Token);
        Assert.Equal(source.GetGrainId(), first.Token.Sender);
        Assert.Equal(first.Token, second.Token);
    }

    [Theory]
    [InlineData("token-selector")]
    [InlineData("grouped-token-selector")]
    public async Task Routing_can_use_both_the_message_and_its_stable_delivery_token(string mode)
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, target);

        Assert.Equal((0, 1, null), await source.PublishAsync(target, "submitted"));

        var senderDeliveries = fixture.FanOutStreams.Read(StreamId.Create("sender", source.GetGrainId().ToString()));
        Assert.Equal(2, senderDeliveries.Length);
        Assert.Equal(senderDeliveries[0], senderDeliveries[1]);
        var item = Assert.Single(fixture.FanOutStreams.Read(StreamId.Create("item", $"{target}:1")));
        Assert.Equal(new MultiStreamMessage(target, "submitted"), item.Message);
        Assert.NotNull(item.Token);
        Assert.Equal(source.GetGrainId(), item.Token.Sender);
        Assert.Equal(1, item.Token.SequenceNumber);
        Assert.Equal(senderDeliveries[0].Token, item.Token);
    }

    [Theory]
    [InlineData("selector")]
    [InlineData("grouped-selector")]
    public async Task Routing_is_evaluated_for_each_message(string mode)
    {
        var firstTarget = Guid.NewGuid();
        var secondTarget = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, firstTarget);

        await source.PublishAsync(firstTarget, "first-message");
        Assert.Equal((0, 2, null), await source.PublishAsync(secondTarget, "second-message"));

        var firstDeliveries = fixture.FanOutStreams.Read(StreamId.Create("first", firstTarget));
        var secondDeliveries = fixture.FanOutStreams.Read(StreamId.Create("first", secondTarget));
        Assert.Equal(2, firstDeliveries.Length);
        Assert.Equal(2, secondDeliveries.Length);
        Assert.All(firstDeliveries, delivery => Assert.Equal(new MultiStreamMessage(firstTarget, "first-message"), delivery.Message));
        Assert.All(secondDeliveries, delivery => Assert.Equal(new MultiStreamMessage(secondTarget, "second-message"), delivery.Message));
        Assert.Single(fixture.FanOutStreams.Read(StreamId.Create("second", firstTarget)));
        Assert.Single(fixture.FanOutStreams.Read(StreamId.Create("second", secondTarget)));
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("grouped-fixed")]
    [InlineData("selector")]
    [InlineData("grouped-selector")]
    [InlineData("token-selector")]
    [InlineData("grouped-token-selector")]
    public async Task Empty_destinations_are_a_successful_noop(string mode)
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, target, empty: true);

        Assert.Equal((0, 1, null), await source.PublishAsync(target, "ignored"));

        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("first", target)));
        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("second", target)));
        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("sender", source.GetGrainId().ToString())));
        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("item", $"{target}:1")));
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("selector")]
    public async Task Partial_failure_keeps_the_item_pending_and_retry_preserves_its_identity(string mode)
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, target);
        var firstId = StreamId.Create("first", target);
        var secondId = StreamId.Create("second", target);
        fixture.FanOutStreams.FailNext(secondId);

        Assert.Equal((1, 0, nameof(IOException)), await source.PublishAsync(target, "retry-me"));

        var initial = Assert.Single(fixture.FanOutStreams.Read(firstId));
        Assert.Empty(fixture.FanOutStreams.Read(secondId));
        Assert.Equal((0, 1, nameof(IOException)), await source.RetryAsync());
        var retried = fixture.FanOutStreams.Read(firstId);
        Assert.Equal(3, retried.Length);
        var second = Assert.Single(fixture.FanOutStreams.Read(secondId));
        Assert.NotNull(initial.Token);
        Assert.All(retried, delivery => Assert.Equal(initial, delivery));
        Assert.Equal(initial.Token, second.Token);
        Assert.Equal(initial.Message, second.Message);
    }

    [Fact]
    public async Task Null_destination_selection_does_not_publish_or_acknowledge()
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync("null-result", target);

        Assert.Equal((1, 0, nameof(InvalidOperationException)), await source.PublishAsync(target, "pending"));

        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("first", target)));
        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("second", target)));
    }

    [Theory]
    [InlineData("fixed-throwing-enumeration")]
    [InlineData("throwing-enumeration")]
    public async Task Lazy_destinations_publish_until_enumeration_fails_and_retry_with_the_same_identity(string mode)
    {
        var target = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IMultiStreamPostmanGrain>(Guid.NewGuid());
        await source.ConfigureAsync(mode, target);
        var firstId = StreamId.Create("first", target);

        Assert.Equal((1, 0, nameof(InvalidOperationException)), await source.PublishAsync(target, "pending"));

        var initial = Assert.Single(fixture.FanOutStreams.Read(firstId));
        Assert.Equal(new MultiStreamMessage(target, "pending"), initial.Message);
        Assert.NotNull(initial.Token);
        Assert.Empty(fixture.FanOutStreams.Read(StreamId.Create("second", target)));
        Assert.Equal((1, 0, nameof(InvalidOperationException)), await source.RetryAsync());
        var retried = fixture.FanOutStreams.Read(firstId);
        Assert.Equal(2, retried.Length);
        Assert.All(retried, delivery => Assert.Equal(initial, delivery));
    }
}

[GenerateSerializer]
public sealed record MultiStreamMessage([property: Id(0)] Guid Target, [property: Id(1)] string Value);

public interface IMultiStreamPostmanGrain : IGrainWithGuidKey
{
    Task ConfigureAsync(string mode, Guid target, bool empty = false);
    Task<(int Pending, int Acknowledged, string? Failure)> PublishAsync(Guid target, string value);
    Task<(int Pending, int Acknowledged, string? Failure)> RetryAsync();
}

public sealed class MultiStreamPostmanGrain : Grain, IMultiStreamPostmanGrain, IOutboxGrain
{
    private Outbox<MultiStreamMessage> outbox = Outbox<MultiStreamMessage>.Create();
    private OutboxProcessor<MultiStreamMessage>? processor;
    private int acknowledged;
    private string? failure;

    public Task ConfigureAsync(string mode, Guid target, bool empty = false)
    {
        processor = this.RegisterOutboxProcessor(() => outbox, options =>
        {
            options.RetryDelay = TimeSpan.FromMinutes(10);
            options.AcknowledgePosted = items =>
            {
                outbox = outbox.RemoveRange(items);
                acknowledged += items.Length;
            };
            options.AcknowledgeFailuresAsync = (failures, _) =>
            {
                failure = failures[0].Error.GetType().Name;
                return ValueTask.CompletedTask;
            };
        });

        const string provider = FakePublishingStreamProvider.ProviderName;
        IEnumerable<StreamId> fixedDestinations = empty ? [] : Destinations(target);
        IEnumerable<StreamId> Select(MultiStreamMessage message) => empty ? [] : Destinations(message.Target);
        IEnumerable<StreamId> SelectWithToken(MultiStreamMessage message, OutboxSequenceToken token) => empty ? [] :
            [StreamId.Create("sender", token.Sender.ToString()), StreamId.Create("item", $"{message.Target}:{token.SequenceNumber}"),
                StreamId.Create("sender", token.Sender.ToString())];

        switch (mode)
        {
            case "fixed": processor.AddStreamPostman<MultiStreamMessage>(provider, fixedDestinations); break;
            case "grouped-fixed": processor.ForStreamProvider(provider).AddStreamPostman<MultiStreamMessage>(fixedDestinations); break;
            case "selector": processor.AddStreamPostman<MultiStreamMessage>(provider, Select); break;
            case "grouped-selector": processor.ForStreamProvider(provider).AddStreamPostman<MultiStreamMessage>(Select); break;
            case "token-selector": processor.AddStreamPostman<MultiStreamMessage>(provider, SelectWithToken); break;
            case "grouped-token-selector": processor.ForStreamProvider(provider).AddStreamPostman<MultiStreamMessage>(SelectWithToken); break;
            case "null-result": processor.AddStreamPostman<MultiStreamMessage>(provider, static _ => (IEnumerable<StreamId>)null!); break;
            case "fixed-throwing-enumeration": processor.AddStreamPostman<MultiStreamMessage>(provider, BrokenDestinations(target)); break;
            case "throwing-enumeration": processor.AddStreamPostman<MultiStreamMessage>(provider, message => BrokenDestinations(message.Target)); break;
            default: throw new ArgumentException($"Unknown registration mode: {mode}", nameof(mode));
        }

        return Task.CompletedTask;
    }

    public Task<(int Pending, int Acknowledged, string? Failure)> PublishAsync(Guid target, string value)
    {
        outbox = outbox.Add(new MultiStreamMessage(target, value));
        return RetryAsync();
    }

    public async Task<(int Pending, int Acknowledged, string? Failure)> RetryAsync()
    {
        await processor!.PostAsync();
        return (outbox.Count, acknowledged, failure);
    }

    private static IEnumerable<StreamId> Destinations(Guid target)
    {
        yield return StreamId.Create("first", target);
        yield return StreamId.Create("second", target);
        yield return StreamId.Create("first", target);
    }

    private static IEnumerable<StreamId> BrokenDestinations(Guid target)
    {
        yield return StreamId.Create("first", target);
        throw new InvalidOperationException("Injected destination enumeration failure.");
    }
}
