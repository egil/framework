using System.Collections.Immutable;
using Egil.Orleans.Messaging.Tests.Outboxes;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public interface IStreamFanOutReceiver : IGrainWithGuidKey
{
    Task<(int Effects, int Attempts)> ReadAsync();
    Task<ImmutableArray<StreamCursor>> DeliveriesAsync();
    Task DeactivateAsync();
}

public sealed class StreamFanOutReceiver : Grain, IStreamFanOutReceiver
{
    private readonly IStateManager<StreamRetryReceiverState> state;
    private readonly StreamManager streams;
    private ImmutableArray<StreamCursor> deliveries = [];

    public StreamFanOutReceiver([PersistentState("stream-fanout-receiver", "Payload")] IPersistentState<StreamRetryReceiverState> storage)
    {
        state = this.RegisterStateManager("Payload", storage);
        // One durable tracker owns both subscriptions, exercising per-stream receipts rather than separate receiver state.
        streams = this.RegisterStreamManager(() => state.State.Tracker)
            .ConfigureExplicitSubscription<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, "fanout-first", ReceiveAsync,
                options => options.UseTrackedResumeToken = false)
            .ConfigureExplicitSubscription<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, "fanout-second", ReceiveAsync,
                options => options.UseTrackedResumeToken = false);
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken) => streams.EnsureExplicitSubscriptionsAsync(cancellationToken);

    private async ValueTask ReceiveAsync(StreamRetryEvent message, StreamCursor cursor)
    {
        Assert.Null(RequestContext.GetOutboxToken());
        deliveries = deliveries.Add(cursor);
        if (!state.State.Tracker.TryAcceptMessage(cursor, out var tracker))
            return;
        await state.WriteAsync(state.State with { Effects = state.State.Effects + 1, Tracker = tracker });
    }

    public Task<(int Effects, int Attempts)> ReadAsync() => Task.FromResult((state.State.Effects, deliveries.Length));
    public Task<ImmutableArray<StreamCursor>> DeliveriesAsync() => Task.FromResult(deliveries);
    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
