using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Attribute to specify the named configuration for event storage.
/// Similar to Orleans' PersistentState attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class EventStorageAttribute : Attribute
{
    /// <summary>
    /// The name of the event storage configuration.
    /// </summary>
    public string StorageName { get; }

    /// <summary>
    /// Initializes a new instance of EventStorageAttribute.
    /// </summary>
    /// <param name="storageName">The name of the event storage configuration</param>
    public EventStorageAttribute(string storageName)
    {
        StorageName = storageName ?? throw new ArgumentNullException(nameof(storageName));
    }
}

/// <summary>
/// Configuration for event serialization and storage.
/// </summary>
/// <typeparam name="TEvent">The base event type</typeparam>
/// <typeparam name="TOutboxEvent">The base outbox event type</typeparam>
public sealed class EventStorageConfiguration<TEvent, TOutboxEvent>
    where TEvent : class
    where TOutboxEvent : class
{
    /// <summary>
    /// The event serializer to use.
    /// </summary>
    public required IEventSerializer<TEvent> EventSerializer { get; init; }

    /// <summary>
    /// The outbox event serializer to use.
    /// </summary>
    public required IEventSerializer<TOutboxEvent> OutboxEventSerializer { get; init; }

    /// <summary>
    /// The underlying storage provider.
    /// </summary>
    public required IEventStorageProvider StorageProvider { get; init; }

    /// <summary>
    /// Optional configuration for table client settings.
    /// </summary>
    public object? TableClientConfiguration { get; init; }

    /// <summary>
    /// The name of this storage configuration.
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// Interface for event serializers that handle specific event types.
/// </summary>
/// <typeparam name="TEvent">The base event type this serializer handles</typeparam>
public interface IEventSerializer<TEvent>
    where TEvent : class
{
    /// <summary>
    /// Serializes an event to bytes.
    /// </summary>
    byte[] Serialize(TEvent @event);

    /// <summary>
    /// Deserializes bytes to an event.
    /// </summary>
    TEvent Deserialize(byte[] data, string eventTypeName);

    /// <summary>
    /// Gets the type name for an event (used for discrimination).
    /// </summary>
    string GetEventTypeName(TEvent @event);
}

/// <summary>
/// Interface for the underlying storage provider.
/// This abstracts away the specific storage implementation (Azure Tables, SQL, etc.).
/// </summary>
public interface IEventStorageProvider
{
    /// <summary>
    /// Stores an event in the underlying storage.
    /// </summary>
    Task<StoredEventData> StoreEventAsync(
        string grainId, 
        string streamName, 
        byte[] eventData, 
        string eventTypeName, 
        string? deduplicationId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events from storage.
    /// </summary>
    Task<IReadOnlyList<StoredEventData>> GetEventsAsync(
        string grainId, 
        string? streamName = null, 
        long? fromSequenceNumber = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores projection data.
    /// </summary>
    Task StoreProjectionAsync<TProjection>(
        string grainId, 
        byte[] projectionData, 
        string projectionTypeName, 
        long lastSequenceNumber, 
        int version, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads projection data.
    /// </summary>
    Task<StoredProjectionData?> LoadProjectionAsync(
        string grainId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores outbox events.
    /// </summary>
    Task StoreOutboxEventsAsync(
        string grainId, 
        IEnumerable<(byte[] data, string typeName, string? targetStream)> outboxEvents, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending outbox events.
    /// </summary>
    Task<IReadOnlyList<StoredOutboxEventData>> GetPendingOutboxEventsAsync(
        string grainId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes processed outbox events.
    /// </summary>
    Task RemoveOutboxEventsAsync(
        string grainId, 
        IEnumerable<string> outboxEventIds, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates retry information for outbox events.
    /// </summary>
    Task UpdateOutboxEventRetryAsync(
        string grainId, 
        string outboxEventId, 
        int retryCount, 
        DateTime lastRetryAt, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw stored event data from the storage provider.
/// </summary>
public sealed record StoredEventData(
    string GrainId,
    string StreamName,
    long SequenceNumber,
    DateTime Timestamp,
    byte[] EventData,
    string EventTypeName,
    string? DeduplicationId,
    bool IsHandled);

/// <summary>
/// Raw stored projection data from the storage provider.
/// </summary>
public sealed record StoredProjectionData(
    string GrainId,
    byte[] ProjectionData,
    string ProjectionTypeName,
    long LastSequenceNumber,
    int Version);

/// <summary>
/// Raw stored outbox event data from the storage provider.
/// </summary>
public sealed record StoredOutboxEventData(
    string Id,
    string GrainId,
    byte[] EventData,
    string EventTypeName,
    DateTime CreatedAt,
    string? TargetStream,
    int RetryCount,
    DateTime? LastRetryAt);
