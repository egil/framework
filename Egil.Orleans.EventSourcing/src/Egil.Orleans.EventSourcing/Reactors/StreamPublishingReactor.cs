using Egil.Orleans.EventSourcing.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace Egil.Orleans.EventSourcing.Reactors;

/// <summary>
/// A reactor that publishes events to Orleans streams based on stream publication configurations.
/// This reactor integrates with the standard reactor lifecycle and retention policies.
/// </summary>
internal class StreamPublishingReactor<TEvent, TProjection> : IEventReactor<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    private readonly IServiceProvider serviceProvider;
    private readonly StreamPublicationConfiguration publication;

    public string Id { get; }

    public StreamPublishingReactor(
        IServiceProvider serviceProvider, 
        StreamPublicationConfiguration publication,
        string reactorId)
    {
        this.serviceProvider = serviceProvider;
        this.publication = publication;
        Id = reactorId;
    }

    public async ValueTask ReactAsync(
        IEnumerable<TEvent> events,
        TProjection projection,
        IEventReactContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var streamProvider = serviceProvider
                .GetRequiredKeyedService<IStreamProvider>(publication.StreamProvider);

            foreach (var @event in events)
            {
                // Get the stream key for this event
                var streamKey = GetStreamKey(@event, publication.KeySelector);
                
                // Get the stream with proper typing based on key type
                var stream = GetStreamForKey(streamProvider, publication.StreamNamespace, streamKey);
                
                // Publish the event (OnNextAsync takes optional StreamSequenceToken, not CancellationToken)
                await stream.OnNextAsync(@event, token: null);
            }
        }
        catch (Exception ex)
        {
            // If stream publishing fails, this reactor will remain in a failed state
            // and events will be retried according to the reactor retry logic
            throw new InvalidOperationException(
                $"Failed to publish events to stream provider '{publication.StreamProvider}' namespace '{publication.StreamNamespace}'", ex);
        }
    }

    private object GetStreamKey(TEvent eventObj, StreamKeySelector? keySelector)
    {
        if (keySelector == null)
        {
            // Default to using a constant key if no selector is provided
            return "default";
        }

        return keySelector.GetKey(eventObj);
    }

    private IAsyncStream<TEvent> GetStreamForKey(IStreamProvider streamProvider, string streamNamespace, object streamKey)
    {
        // Handle different key types using the appropriate Orleans extension methods
        return streamKey switch
        {
            string stringKey => streamProvider.GetStream<TEvent>(streamNamespace, stringKey),
            Guid guidKey => streamProvider.GetStream<TEvent>(streamNamespace, guidKey),
            long longKey => streamProvider.GetStream<TEvent>(streamNamespace, longKey),
            byte[] byteKey => throw new NotSupportedException("Byte array keys are not supported for Orleans streams"),
            _ => throw new InvalidOperationException($"Unsupported stream key type: {streamKey.GetType()}")
        };
    }
}