#pragma warning disable ORLEANSEXP005 // This suite explicitly exercises the pinned experimental journaling package.
using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class ImmediateOutboxTests(JournalingPrototypeFixture fixture)
    : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task The_grain_can_post_and_acknowledge_messages_without_saving()
    {
        var grain = fixture.NewImmediateGrain();

        var remaining = await grain.PostWithoutSavingAsync("immediate", TestContext.Current.CancellationToken);

        Assert.Equal("immediate", Assert.Single(fixture.Delivered.For(grain.GetGrainId())).Text);
        Assert.Empty(remaining);
        Assert.Empty(fixture.Storage.For(grain.GetGrainId()).Writes);
    }
}

public interface IImmediateOutboxGrain : IGrainWithGuidKey
{
    Task<Outbox<OrderEvent>> PostWithoutSavingAsync(string text, CancellationToken cancellationToken);
}

public sealed class ImmediateOutboxGrain : DurableGrain, IImmediateOutboxGrain, IOutboxGrain
{
    private readonly IDurableOutbox<OrderEvent> outbox;
    private readonly OutboxProcessor<OrderEvent> processor;

    public ImmediateOutboxGrain(
        [FromKeyedServices("outbox")] IDurableOutbox<OrderEvent> outbox,
        DeliveredMessages delivered)
    {
        this.outbox = outbox;
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OrderEvent>
        {
            OutboxAccessor = () => outbox.AsImmutable(),
            AcknowledgePostedAsync = (items, _) =>
            {
                // This grain deliberately chooses immediate delivery and defers all persistence.
                outbox.RemoveRange(items);
                return ValueTask.CompletedTask;
            }
        }).AddPostman<OrderEvent>(message => delivered.DeliverAsync(this.GetGrainId(), message));
    }

    public async Task<Outbox<OrderEvent>> PostWithoutSavingAsync(string text, CancellationToken cancellationToken)
    {
        outbox.Add(new OrderEvent(text));
        await processor.PostAsync(cancellationToken);
        return outbox.AsImmutable();
    }
}
