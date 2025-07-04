using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Storage;

/// <summary>
/// Strongly-typed event storage implementation that wraps configuration and provides type safety.
/// </summary>
/// <typeparam name="TEvent">The domain event type</typeparam>
/// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
internal sealed class TypedEventStorage<TEvent, TOutboxEvent> : IEventStorage<TEvent, TOutboxEvent>
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly EventStorageConfiguration<TEvent, TOutboxEvent> configuration;
    private readonly IEventStorage underlyingStorage;
    private readonly IGrainContext grainContext;
    private readonly ILogger<TypedEventStorage<TEvent, TOutboxEvent>> logger;

    public TypedEventStorage(
        EventStorageConfiguration<TEvent, TOutboxEvent> configuration,
        IGrainContext grainContext,
        ILogger<TypedEventStorage<TEvent, TOutboxEvent>> logger)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.grainContext = grainContext ?? throw new ArgumentNullException(nameof(grainContext));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Create the underlying storage using the provider
        this.underlyingStorage = configuration.StorageProvider.Create<object>(grainContext);
    }

    public async ValueTask<AppendEventsResult> AppendEventsAsync(
        IEnumerable<TEvent> events, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var serializedEvents = events.Select(e => SerializeEvent(e, configuration.EventSerializer));
        var result = await underlyingStorage.AppendEventsAsync(serializedEvents, cancellationToken);
        
        return result;
    }

    public async ValueTask<AppendEventsResult> AppendOutboxEventsAsync(
        IEnumerable<TOutboxEvent> events, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var serializedEvents = events.Select(e => SerializeEvent(e, configuration.OutboxEventSerializer));
        var result = await underlyingStorage.AppendEventsAsync(serializedEvents, cancellationToken);
        
        return result;
    }

    public async IAsyncEnumerable<StoredEvent<TEvent>> ReadEventsAsync(
        long fromSequenceNumber = 0, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var storedEvent in underlyingStorage.ReadEventsAsync(fromSequenceNumber, cancellationToken))
        {
            if (TryDeserializeEvent<TEvent>(storedEvent, configuration.EventSerializer, out var domainEvent))
            {
                yield return new StoredEvent<TEvent>(
                    domainEvent,
                    storedEvent.SequenceNumber,
                    storedEvent.Timestamp,
                    storedEvent.EventId,
                    storedEvent.Metadata);
            }
        }
    }

    public async IAsyncEnumerable<StoredEvent<TOutboxEvent>> ReadOutboxEventsAsync(
        long fromSequenceNumber = 0, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var storedEvent in underlyingStorage.ReadEventsAsync(fromSequenceNumber, cancellationToken))
        {
            if (TryDeserializeEvent<TOutboxEvent>(storedEvent, configuration.OutboxEventSerializer, out var outboxEvent))
            {
                yield return new StoredEvent<TOutboxEvent>(
                    outboxEvent,
                    storedEvent.SequenceNumber,
                    storedEvent.Timestamp,
                    storedEvent.EventId,
                    storedEvent.Metadata);
            }
        }
    }

    public async ValueTask<ProjectionState<TProjection>?> ReadProjectionAsync<TProjection>(
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        if (underlyingStorage is IProjectionStorage projectionStorage)
        {
            return await projectionStorage.ReadProjectionAsync<TProjection>(cancellationToken);
        }

        throw new NotSupportedException($"The underlying storage provider does not support projection storage. " +
                                       $"Use Orleans IPersistentState<T> for projection storage instead.");
    }

    public async ValueTask WriteProjectionAsync<TProjection>(
        ProjectionState<TProjection> projectionState,
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        if (underlyingStorage is IProjectionStorage projectionStorage)
        {
            await projectionStorage.WriteProjectionAsync(projectionState, cancellationToken);
            return;
        }

        throw new NotSupportedException($"The underlying storage provider does not support projection storage. " +
                                       $"Use Orleans IPersistentState<T> for projection storage instead.");
    }

    public async ValueTask AddOutboxEventsAsync(
        string grainId, 
        IEnumerable<OutboxEvent> outboxEvents, 
        CancellationToken cancellationToken = default)
    {
        if (underlyingStorage is ILegacyEventStorage legacyStorage)
        {
            await legacyStorage.AddOutboxEventsAsync(grainId, outboxEvents, cancellationToken);
            return;
        }

        throw new NotSupportedException($"The underlying storage provider does not support outbox storage.");
    }

    public async ValueTask<IReadOnlyList<OutboxEvent>> GetPendingOutboxEventsAsync(
        string grainId, 
        CancellationToken cancellationToken = default)
    {
        if (underlyingStorage is ILegacyEventStorage legacyStorage)
        {
            return await legacyStorage.GetPendingOutboxEventsAsync(grainId, cancellationToken);
        }

        throw new NotSupportedException($"The underlying storage provider does not support outbox storage.");
    }

    public async ValueTask RemoveOutboxEventsAsync(
        string grainId, 
        IEnumerable<string> outboxEventIds, 
        CancellationToken cancellationToken = default)
    {
        if (underlyingStorage is ILegacyEventStorage legacyStorage)
        {
            await legacyStorage.RemoveOutboxEventsAsync(grainId, outboxEventIds, cancellationToken);
            return;
        }

        throw new NotSupportedException($"The underlying storage provider does not support outbox storage.");
    }

    public async ValueTask UpdateOutboxEventRetryAsync(
        string grainId, 
        string outboxEventId, 
        int retryCount, 
        DateTime lastRetryAt, 
        CancellationToken cancellationToken = default)
    {
        if (underlyingStorage is ILegacyEventStorage legacyStorage)
        {
            await legacyStorage.UpdateOutboxEventRetryAsync(grainId, outboxEventId, retryCount, lastRetryAt, cancellationToken);
            return;
        }

        throw new NotSupportedException($"The underlying storage provider does not support outbox storage.");
    }

    // IEventStorage implementation for backward compatibility
    public ValueTask<AppendEventsResult> AppendEventsAsync(
        IEnumerable<object> events, 
        CancellationToken cancellationToken = default) =>
        underlyingStorage.AppendEventsAsync(events, cancellationToken);

    public IAsyncEnumerable<StoredEvent<object>> ReadEventsAsync(
        long fromSequenceNumber = 0, 
        CancellationToken cancellationToken = default) =>
        underlyingStorage.ReadEventsAsync(fromSequenceNumber, cancellationToken);

    private static object SerializeEvent<T>(T @event, IEventSerializer<T> serializer)
        where T : class
    {
        // For now, return the event as-is. In a real implementation,
        // this would serialize to bytes or a storage-specific format
        return @event;
    }

    private static bool TryDeserializeEvent<T>(
        StoredEvent<object> storedEvent, 
        IEventSerializer<T> serializer,
        out T deserializedEvent)
        where T : class
    {
        deserializedEvent = default!;

        if (storedEvent.Event is T typedEvent)
        {
            deserializedEvent = typedEvent;
            return true;
        }

        // In a real implementation, this would attempt deserialization
        // from bytes or storage-specific format
        try
        {
            if (storedEvent.Event is string json)
            {
                deserializedEvent = serializer.Deserialize(json);
                return deserializedEvent is not null;
            }
        }
        catch (Exception ex)
        {
            // Log deserialization failure
            return false;
        }

        return false;
    }
}

/// <summary>
/// Extension interface for storage providers that support projection storage.
/// </summary>
public interface IProjectionStorage
{
    /// <summary>
    /// Reads projection state from storage.
    /// </summary>
    ValueTask<ProjectionState<TProjection>?> ReadProjectionAsync<TProjection>(
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;

    /// <summary>
    /// Writes projection state to storage.
    /// </summary>
    ValueTask WriteProjectionAsync<TProjection>(
        ProjectionState<TProjection> projectionState,
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;
}
