using System.Collections.Immutable;
using Orleans.Concurrency;
using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public interface IJournaledOrderGrain : IGrainWithGuidKey
{
    Task<bool> ReceiveAsync(OutboxSequenceToken token, string text, CancellationToken cancellationToken);
    Task PostAsync(CancellationToken cancellationToken);
    Task AcknowledgeAsync(ImmutableArray<OutboxMessageId> ids, CancellationToken cancellationToken);
    Task ClearOutboxAsync(CancellationToken cancellationToken);
    Task EvictAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
    [AlwaysInterleave] Task<Outbox<OrderEvent>> ReadDispatchableAsync();
    Task<OrderView> ReadAsync();
    Task DeactivateAsync();
}

public sealed class JournaledOrderGrain : DurableGrain, IJournaledOrderGrain, IOutboxGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly IDurableValue<OrderState> business;
    private readonly IDurableMessageTracker tracker;
    private readonly IDurableOutbox<OrderEvent> outbox;
    private readonly OutboxProcessor<OrderEvent> processor;

    public JournaledOrderGrain(
        [FromKeyedServices("business")] IDurableValue<OrderState> business,
        [FromKeyedServices("tracker")] IDurableMessageTracker tracker,
        [FromKeyedServices("outbox")] IDurableOutbox<OrderEvent> outbox,
        DeliveredMessages delivered)
    {
        this.business = business;
        this.tracker = tracker;
        this.outbox = outbox;
        processor = this.RegisterOutboxProcessor(() => outbox.AsImmutable(), options =>
        {
            options.AcknowledgePostedAsync = async (items, ct) =>
                await AcknowledgeAsync(items.Select(item => item.Id).ToImmutableArray(), ct);
        }).AddPostman<OrderEvent>(message => delivered.DeliverAsync(this.GetGrainId(), message));
    }

    public async Task<bool> ReceiveAsync(OutboxSequenceToken token, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!tracker.TryAcceptMessage(token))
            {
                return false;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            business.Value = new OrderState(checked((business.Value?.Accepted ?? 0) + 1));
            outbox.Add(new OrderEvent(text));
            // All three named components share this manager. No OM IStateManager participates.
            await WriteStateAsync(cancellationToken);
            return true;
        }
        catch
        {
            // Business processing can fail after the tracker staged acceptance, and a lost
            // write acknowledgement is uncertain. Recover before handling another message.
            DeactivateOnIdle();
            throw;
        }
    }

    public Task PostAsync(CancellationToken cancellationToken) => processor.PostAsync(cancellationToken).AsTask();

    public async Task AcknowledgeAsync(ImmutableArray<OutboxMessageId> ids, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            outbox.RemoveRange(ids);
            await WriteStateAsync(cancellationToken);
        }
        catch
        {
            DeactivateOnIdle();
            throw;
        }
    }

    public async Task ClearOutboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            outbox.Clear();
            await WriteStateAsync(cancellationToken);
        }
        catch
        {
            DeactivateOnIdle();
            throw;
        }
    }

    public async Task EvictAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Evict(olderThan);
            await WriteStateAsync(cancellationToken);
        }
        catch
        {
            DeactivateOnIdle();
            throw;
        }
    }

    public Task<Outbox<OrderEvent>> ReadDispatchableAsync() => Task.FromResult(outbox.AsImmutable());
    public Task<OrderView> ReadAsync() => Task.FromResult(new OrderView(
        activation, business.Value ?? new OrderState(0), tracker.AsImmutable(), outbox.AsImmutable()));

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

[GenerateSerializer]
public sealed record OrderState([property: Id(0)] int Accepted);

[GenerateSerializer]
public sealed record OrderEvent([property: Id(0)] string Text);

[GenerateSerializer]
public sealed record OrderView(
    [property: Id(0)] Guid Activation,
    [property: Id(1)] OrderState Business,
    [property: Id(2)] MessageTracker Tracker,
    [property: Id(3)] Outbox<OrderEvent> Outbox);
