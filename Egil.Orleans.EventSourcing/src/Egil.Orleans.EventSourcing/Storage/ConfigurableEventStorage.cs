using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.EventSourcing.Storage;

/// <summary>
/// Event storage implementation that uses named configurations.
/// Similar to Orleans' named persistent state pattern.
/// </summary>
/// <typeparam name="TEvent">The base event type</typeparam>
/// <typeparam name="TOutboxEvent">The base outbox event type</typeparam>
public sealed class ConfigurableEventStorage<TEvent, TOutboxEvent> : IEventStorage
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly EventStorageConfiguration<TEvent, TOutboxEvent> configuration;

    /// <summary>
    /// Initializes a new instance of ConfigurableEventStorage.
    /// </summary>
    public ConfigurableEventStorage(EventStorageConfiguration<TEvent, TOutboxEvent> configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Appends an event to the specified stream.
    /// </summary>
    public async Task<StoredEvent> AppendEventAsync(string grainId, string streamName, object @event, string? deduplicationId, CancellationToken cancellationToken = default)
    {
        if (@event is not TEvent typedEvent)
            throw new ArgumentException($"Event must be of type {typeof(TEvent).Name}", nameof(@event));

        var eventData = configuration.EventSerializer.Serialize(typedEvent);
        var eventTypeName = configuration.EventSerializer.GetEventTypeName(typedEvent);

        var storedData = await configuration.StorageProvider.StoreEventAsync(
            grainId, streamName, eventData, eventTypeName, deduplicationId, cancellationToken);

        return new StoredEvent(
            storedData.GrainId,
            storedData.StreamName,
            storedData.SequenceNumber,
            storedData.Timestamp,
            typedEvent, // Return the original typed event
            storedData.DeduplicationId,
            storedData.IsHandled,
            storedData.EventTypeName);
    }

    /// <summary>
    /// Retrieves all events for a grain across all streams, ordered by sequence number.
    /// </summary>
    public async Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, CancellationToken cancellationToken = default)
    {
        var storedData = await configuration.StorageProvider.GetEventsAsync(grainId, null, null, cancellationToken);
        return storedData.Select(DeserializeStoredEvent).ToList();
    }

    /// <summary>
    /// Retrieves events for a specific stream of a grain.
    /// </summary>
    public async Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, string streamName, CancellationToken cancellationToken = default)
    {
        var storedData = await configuration.StorageProvider.GetEventsAsync(grainId, streamName, null, cancellationToken);
        return storedData.Select(DeserializeStoredEvent).ToList();
    }

    /// <summary>
    /// Retrieves events for a grain starting from a specific sequence number.
    /// </summary>
    public async Task<IReadOnlyList<StoredEvent>> GetEventsFromSequenceAsync(string grainId, long fromSequenceNumber, CancellationToken cancellationToken = default)
    {
        var storedData = await configuration.StorageProvider.GetEventsAsync(grainId, null, fromSequenceNumber, cancellationToken);
        return storedData.Select(DeserializeStoredEvent).ToList();
    }

    /// <summary>
    /// Removes events from storage.
    /// </summary>
    public Task RemoveEventsAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> eventsToRemove, CancellationToken cancellationToken = default)
    {
        // Implementation would delegate to storage provider
        throw new NotImplementedException("Event removal not yet implemented");
    }

    /// <summary>
    /// Marks events as handled in a batch operation.
    /// </summary>
    public Task MarkEventsAsHandledAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> events, CancellationToken cancellationToken = default)
    {
        // Implementation would delegate to storage provider
        throw new NotImplementedException("Mark events as handled not yet implemented");
    }

    /// <summary>
    /// Finds an existing event by deduplication ID in the specified stream.
    /// </summary>
    public async Task<StoredEvent?> FindEventByDeduplicationIdAsync(string grainId, string streamName, string deduplicationId, CancellationToken cancellationToken = default)
    {
        // Implementation would search for event by deduplication ID
        throw new NotImplementedException("Find by deduplication ID not yet implemented");
    }

    /// <summary>
    /// Stores projection data for grains using shared transaction scope.
    /// </summary>
    public async Task StoreProjectionAsync<TProjection>(string grainId, TProjection projection, long lastSequenceNumber, int version, CancellationToken cancellationToken = default)
        where TProjection : notnull
    {
        // Serialize projection using JSON (could be configurable)
        var projectionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(projection);
        var projectionTypeName = typeof(TProjection).FullName ?? typeof(TProjection).Name;

        await configuration.StorageProvider.StoreProjectionAsync(
            grainId, projectionData, projectionTypeName, lastSequenceNumber, version, cancellationToken);
    }

    /// <summary>
    /// Loads projection data for grains using shared transaction scope.
    /// </summary>
    public async Task<(TProjection? projection, long lastSequenceNumber, int version)?> LoadProjectionAsync<TProjection>(string grainId, CancellationToken cancellationToken = default)
        where TProjection : notnull
    {
        var storedData = await configuration.StorageProvider.LoadProjectionAsync(grainId, cancellationToken);
        
        if (storedData == null)
            return null;

        // Deserialize projection using JSON (could be configurable)
        var projection = System.Text.Json.JsonSerializer.Deserialize<TProjection>(storedData.ProjectionData);
        
        return (projection, storedData.LastSequenceNumber, storedData.Version);
    }

    /// <summary>
    /// Adds events to the outbox storage.
    /// </summary>
    public async Task AddOutboxEventsAsync(string grainId, IEnumerable<OutboxEvent> outboxEvents, CancellationToken cancellationToken = default)
    {
        var serializedEvents = outboxEvents.Select(evt =>
        {
            if (evt.Event is not TOutboxEvent typedEvent)
                throw new ArgumentException($"Outbox event must be of type {typeof(TOutboxEvent).Name}");

            var eventData = configuration.OutboxEventSerializer.Serialize(typedEvent);
            var eventTypeName = configuration.OutboxEventSerializer.GetEventTypeName(typedEvent);

            return (eventData, eventTypeName, evt.TargetStream);
        });

        await configuration.StorageProvider.StoreOutboxEventsAsync(grainId, serializedEvents, cancellationToken);
    }

    /// <summary>
    /// Retrieves pending outbox events for processing.
    /// </summary>
    public async Task<IReadOnlyList<OutboxEvent>> GetPendingOutboxEventsAsync(string grainId, CancellationToken cancellationToken = default)
    {
        var storedData = await configuration.StorageProvider.GetPendingOutboxEventsAsync(grainId, cancellationToken);
        
        return storedData.Select(data =>
        {
            var deserializedEvent = configuration.OutboxEventSerializer.Deserialize(data.EventData, data.EventTypeName);
            
            return new OutboxEvent(
                data.Id,
                data.GrainId,
                deserializedEvent,
                data.CreatedAt,
                data.EventTypeName,
                data.TargetStream,
                data.RetryCount,
                data.LastRetryAt);
        }).ToList();
    }

    /// <summary>
    /// Removes processed outbox events.
    /// </summary>
    public Task RemoveOutboxEventsAsync(string grainId, IEnumerable<string> outboxEventIds, CancellationToken cancellationToken = default)
    {
        return configuration.StorageProvider.RemoveOutboxEventsAsync(grainId, outboxEventIds, cancellationToken);
    }

    /// <summary>
    /// Updates retry information for failed outbox events.
    /// </summary>
    public Task UpdateOutboxEventRetryAsync(string grainId, string outboxEventId, int retryCount, DateTime lastRetryAt, CancellationToken cancellationToken = default)
    {
        return configuration.StorageProvider.UpdateOutboxEventRetryAsync(grainId, outboxEventId, retryCount, lastRetryAt, cancellationToken);
    }

    private StoredEvent DeserializeStoredEvent(StoredEventData data)
    {
        var deserializedEvent = configuration.EventSerializer.Deserialize(data.EventData, data.EventTypeName);
        
        return new StoredEvent(
            data.GrainId,
            data.StreamName,
            data.SequenceNumber,
            data.Timestamp,
            deserializedEvent,
            data.DeduplicationId,
            data.IsHandled,
            data.EventTypeName);
    }
}
