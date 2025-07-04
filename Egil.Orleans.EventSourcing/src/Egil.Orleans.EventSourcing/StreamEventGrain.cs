using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Abstract base class for event-sourced grains that also subscribe to Orleans streams.
/// Inherits from EventGrain and adds Orleans stream subscription capabilities.
/// </summary>
/// <typeparam name="TProjection">The type of the immutable projection state</typeparam>
public abstract class StreamEventGrain<TProjection> : EventGrain<TProjection>, IAsyncObserver<object>
    where TProjection : notnull
{
    protected StreamEventGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<TProjection>> projectionState,
        IEventStorage eventStorage,
        IOutboxStorage outboxStorage,
        IEventPublisher eventPublisher,
        ILogger<StreamEventGrain<TProjection>> logger)
        : base(projectionState, eventStorage, outboxStorage, eventPublisher, logger)
    {
    }

    #region Orleans Stream Integration

    /// <summary>
    /// Handles events received from Orleans streams.
    /// Default implementation forwards to ProcessEventAsync.
    /// </summary>
    public virtual async Task OnNextAsync(object @event, StreamSequenceToken? token = null)
    {
        await ProcessEventAsync(@event);
    }

    public virtual Task OnCompletedAsync() => Task.CompletedTask;

    public virtual Task OnErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Stream error in StreamEventGrain {GrainId}", this.GetPrimaryKeyString());
        return Task.CompletedTask;
    }

    #endregion

    #region Stream Subscription Helpers

    /// <summary>
    /// Helper method to subscribe to a stream. Call this in OnActivateAsync.
    /// </summary>
    protected async Task SubscribeToStreamAsync(string streamProviderName, string streamNamespace, string streamKey)
    {
        var streamProvider = this.GetStreamProvider(streamProviderName);
        var stream = streamProvider.GetStream<object>(streamNamespace, streamKey);
        await stream.SubscribeAsync(this);
    }

    /// <summary>
    /// Helper method to subscribe to a stream using the grain's primary key. Call this in OnActivateAsync.
    /// </summary>
    protected async Task SubscribeToStreamAsync(string streamProviderName, string streamNamespace)
    {
        await SubscribeToStreamAsync(streamProviderName, streamNamespace, this.GetPrimaryKeyString());
    }

    /// <summary>
    /// Helper method to subscribe to multiple streams. Call this in OnActivateAsync.
    /// </summary>
    protected async Task SubscribeToStreamsAsync(params StreamSubscription[] subscriptions)
    {
        var tasks = subscriptions.Select(async sub =>
        {
            var streamProvider = this.GetStreamProvider(sub.StreamProviderName);
            var stream = streamProvider.GetStream<object>(sub.StreamNamespace, sub.StreamKey ?? this.GetPrimaryKeyString());
            await stream.SubscribeAsync(this);
        });

        await Task.WhenAll(tasks);
    }

    #endregion

    /// <summary>
    /// Provides access to the logger for derived classes.
    /// </summary>
    protected ILogger Logger => logger;
}

/// <summary>
/// Configuration for stream subscriptions.
/// </summary>
/// <param name="StreamProviderName">The name of the stream provider</param>
/// <param name="StreamNamespace">The stream namespace</param>
/// <param name="StreamKey">The stream key (uses grain primary key if null)</param>
public sealed record StreamSubscription(
    string StreamProviderName,
    string StreamNamespace,
    string? StreamKey = null);
