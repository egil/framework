using Orleans.Runtime;
using System.Collections.Concurrent;

namespace Egil.Orleans.EventSourcing.InMemory;

/// <summary>
/// In-memory event storage provider for testing and development.
/// Not suitable for production use.
/// </summary>
public class InMemoryEventStorageProvider : IEventStorageProvider
{
    private readonly ConcurrentDictionary<string, InMemoryEventStorage> storages = new();

    public IEventStorage<TEvent> Create<TEvent>(IGrainContext grainContext)
        where TEvent : class
    {
        var grainId = grainContext.GrainId.ToString();
        var storage = storages.GetOrAdd(grainId, _ => new InMemoryEventStorage());
        return new InMemoryEventStorage<TEvent>(storage);
    }
}

/// <summary>
/// In-memory event storage implementation.
/// </summary>
public class InMemoryEventStorage : ILegacyEventStorage
{
    private readonly List<StoredEvent> events = new();
    private readonly Dictionary<string, ProjectionData<object>> projections = new();
    private readonly List<OutboxEvent> outboxEvents = new();
    private long nextSequenceNumber = 1;

    public async Task<StoredEvent> AppendEventAsync(
        string grainId, 
        string streamName, 
        object @event, 
        string? deduplicationId, 
        CancellationToken cancellationToken = default)
    {
        // Check for duplicate if deduplication ID is provided
        if (!string.IsNullOrEmpty(deduplicationId))
        {
            var existing = await FindEventByDeduplicationIdAsync(grainId, streamName, deduplicationId, cancellationToken);
            if (existing != null)
            {
                return existing;
            }
        }

        var storedEvent = new StoredEvent(
            Event: @event,
            SequenceNumber: nextSequenceNumber++,
            Timestamp: DateTimeOffset.UtcNow,
            StreamName: streamName,
            DeduplicationId: deduplicationId,
            EventId: Guid.NewGuid().ToString());

        events.Add(storedEvent);
        return storedEvent;
    }

    public Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, CancellationToken cancellationToken = default)
    {
        var grainEvents = events.Where(e => ExtractGrainId(e) == grainId).ToList();
        return Task.FromResult<IReadOnlyList<StoredEvent>>(grainEvents);
    }

    public Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, string streamName, CancellationToken cancellationToken = default)
    {
        var grainEvents = events
            .Where(e => ExtractGrainId(e) == grainId && e.StreamName == streamName)
            .ToList();
        return Task.FromResult<IReadOnlyList<StoredEvent>>(grainEvents);
    }

    public Task<IReadOnlyList<StoredEvent>> GetEventsFromSequenceAsync(string grainId, long fromSequenceNumber, CancellationToken cancellationToken = default)
    {
        var grainEvents = events
            .Where(e => ExtractGrainId(e) == grainId && e.SequenceNumber >= fromSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .ToList();
        return Task.FromResult<IReadOnlyList<StoredEvent>>(grainEvents);
    }

    public Task RemoveEventsAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> eventsToRemove, CancellationToken cancellationToken = default)
    {
        var toRemove = eventsToRemove.ToHashSet();
        events.RemoveAll(e => ExtractGrainId(e) == grainId && 
                              toRemove.Contains((e.StreamName, e.SequenceNumber)));
        return Task.CompletedTask;
    }

    public Task MarkEventsAsHandledAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> events, CancellationToken cancellationToken = default)
    {
        var toMark = events.ToHashSet();
        for (int i = 0; i < this.events.Count; i++)
        {
            var evt = this.events[i];
            if (ExtractGrainId(evt) == grainId && toMark.Contains((evt.StreamName, evt.SequenceNumber)))
            {
                this.events[i] = evt with { IsHandled = true };
            }
        }
        return Task.CompletedTask;
    }

    public Task<StoredEvent?> FindEventByDeduplicationIdAsync(string grainId, string streamName, string deduplicationId, CancellationToken cancellationToken = default)
    {
        var evt = events.FirstOrDefault(e => 
            ExtractGrainId(e) == grainId && 
            e.StreamName == streamName && 
            e.DeduplicationId == deduplicationId);
        return Task.FromResult(evt);
    }

    public Task StoreProjectionAsync<TProjection>(string grainId, TProjection projection, long lastSequenceNumber, int version, CancellationToken cancellationToken = default) where TProjection : notnull
    {
        projections[grainId] = new ProjectionData<object>(projection, lastSequenceNumber, version);
        return Task.CompletedTask;
    }

    public Task<ProjectionData<TProjection>?> LoadProjectionAsync<TProjection>(string grainId, CancellationToken cancellationToken = default) where TProjection : notnull
    {
        if (projections.TryGetValue(grainId, out var data) && data.Projection is TProjection typedProjection)
        {
            return Task.FromResult<ProjectionData<TProjection>?>(new ProjectionData<TProjection>(typedProjection, data.LastSequenceNumber, data.Version));
        }
        return Task.FromResult<ProjectionData<TProjection>?>(null);
    }

    public Task<(TProjection? projection, long lastSequenceNumber, int version)?> LoadProjectionAsync<TProjection>(string grainId, CancellationToken cancellationToken = default) where TProjection : notnull
    {
        if (projections.TryGetValue(grainId, out var data) && data.Projection is TProjection typedProjection)
        {
            return Task.FromResult<(TProjection?, long, int)?>((typedProjection, data.LastSequenceNumber, data.Version));
        }
        return Task.FromResult<(TProjection?, long, int)?>(null);
    }

    public Task AddOutboxEventsAsync(string grainId, IEnumerable<OutboxEvent> outboxEvents, CancellationToken cancellationToken = default)
    {
        this.outboxEvents.AddRange(outboxEvents);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxEvent>> GetPendingOutboxEventsAsync(string grainId, CancellationToken cancellationToken = default)
    {
        var pending = outboxEvents.Where(e => e.GrainId == grainId).ToList();
        return Task.FromResult<IReadOnlyList<OutboxEvent>>(pending);
    }

    public Task RemoveOutboxEventsAsync(string grainId, IEnumerable<string> outboxEventIds, CancellationToken cancellationToken = default)
    {
        var idsToRemove = outboxEventIds.ToHashSet();
        outboxEvents.RemoveAll(e => e.GrainId == grainId && idsToRemove.Contains(e.Id));
        return Task.CompletedTask;
    }

    public Task UpdateOutboxEventRetryAsync(string grainId, string outboxEventId, int retryCount, DateTime lastRetryAt, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < outboxEvents.Count; i++)
        {
            var evt = outboxEvents[i];
            if (evt.GrainId == grainId && evt.Id == outboxEventId)
            {
                outboxEvents[i] = evt with { RetryCount = retryCount, LastRetryAt = lastRetryAt };
                break;
            }
        }
        return Task.CompletedTask;
    }

    private static string ExtractGrainId(StoredEvent evt)
    {
        // For simplicity, assume grain ID can be extracted from the event or is stored separately
        // In a real implementation, this would be handled differently
        return "test-grain";
    }
}

/// <summary>
/// Strongly-typed wrapper for in-memory event storage.
/// </summary>
internal class InMemoryEventStorage<TEvent> : IEventStorage<TEvent>
    where TEvent : class
{
    private readonly InMemoryEventStorage storage;

    public InMemoryEventStorage(InMemoryEventStorage storage)
    {
        this.storage = storage;
    }

    public async ValueTask<AppendEventsResult> AppendEventsAsync(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
    {
        var eventsList = events.ToList();
        if (eventsList.Count == 0)
        {
            return new AppendEventsResult(0, 0, 0);
        }

        var firstSequence = 0L;
        var lastSequence = 0L;
        var appended = 0;

        foreach (var evt in eventsList)
        {
            var stored = await storage.AppendEventAsync("test-grain", "default", evt, null, cancellationToken);
            if (appended == 0)
            {
                firstSequence = stored.SequenceNumber;
            }
            lastSequence = stored.SequenceNumber;
            appended++;
        }

        return new AppendEventsResult(firstSequence, lastSequence, appended);
    }

    public async IAsyncEnumerable<StoredEvent<TEvent>> ReadEventsAsync(long fromSequenceNumber = 0, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var events = await storage.GetEventsFromSequenceAsync("test-grain", fromSequenceNumber, cancellationToken);
        
        foreach (var evt in events)
        {
            if (evt.Event is TEvent typedEvent)
            {
                yield return new StoredEvent<TEvent>(
                    typedEvent,
                    evt.SequenceNumber,
                    evt.Timestamp,
                    evt.EventId);
            }
        }
    }

    public ValueTask<AppendEventsResult> AppendEventsAsync(IEnumerable<object> events, CancellationToken cancellationToken = default)
    {
        var typedEvents = events.Cast<TEvent>();
        return AppendEventsAsync(typedEvents, cancellationToken);
    }

    public IAsyncEnumerable<StoredEvent<object>> ReadEventsAsync(long fromSequenceNumber = 0, CancellationToken cancellationToken = default)
    {
        return ReadEventsAsync(fromSequenceNumber, cancellationToken)
            .Select(e => new StoredEvent<object>(e.Event, e.SequenceNumber, e.Timestamp, e.EventId))
            .ToAsyncEnumerable();
    }
}

/// <summary>
/// Extension methods for registering in-memory event storage.
/// </summary>
public static class InMemoryEventStorageExtensions
{
    /// <summary>
    /// Configures the event storage to use an in-memory provider.
    /// </summary>
    public static EventStorageConfigurationBuilder<TEvent, TOutboxEvent> WithInMemoryProvider<TEvent, TOutboxEvent>(
        this EventStorageConfigurationBuilder<TEvent, TOutboxEvent> builder)
        where TEvent : class
        where TOutboxEvent : class
    {
        return builder.UseStorageProvider(new InMemoryEventStorageProvider());
    }

    /// <summary>
    /// Adds a simple event storage configuration with in-memory provider and default serialization.
    /// </summary>
    public static IServiceCollection AddEventStorage<TEvent, TOutboxEvent>(
        this IServiceCollection services,
        string name)
        where TEvent : class
        where TOutboxEvent : class
    {
        return services.AddEventStorageConfiguration<TEvent, TOutboxEvent>(name, builder =>
        {
            builder
                .WithInMemoryProvider()
                .WithJsonSerializer()
                .WithDefaultRetentionPolicy();
        });
    }

    /// <summary>
    /// Configures JSON serialization for events.
    /// </summary>
    public static EventStorageConfigurationBuilder<TEvent, TOutboxEvent> WithJsonSerializer<TEvent, TOutboxEvent>(
        this EventStorageConfigurationBuilder<TEvent, TOutboxEvent> builder)
        where TEvent : class
        where TOutboxEvent : class
    {
        return builder
            .UseEventSerializer(new JsonEventSerializer<TEvent>())
            .UseOutboxEventSerializer(new JsonEventSerializer<TOutboxEvent>());
    }

    /// <summary>
    /// Configures a default retention policy (keep all events).
    /// </summary>
    public static EventStorageConfigurationBuilder<TEvent, TOutboxEvent> WithDefaultRetentionPolicy<TEvent, TOutboxEvent>(
        this EventStorageConfigurationBuilder<TEvent, TOutboxEvent> builder)
        where TEvent : class
        where TOutboxEvent : class
    {
        return builder.UseRetentionPolicy(new KeepAllRetentionPolicy());
    }
}

/// <summary>
/// Simple JSON event serializer for testing.
/// </summary>
public class JsonEventSerializer<T> : IEventSerializer<T>
    where T : class
{
    public string Serialize(T @event)
    {
        return System.Text.Json.JsonSerializer.Serialize(@event);
    }

    public T Deserialize(string data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data)
            ?? throw new InvalidOperationException($"Failed to deserialize event of type {typeof(T).Name}");
    }
}

/// <summary>
/// Simple retention policy that keeps all events.
/// </summary>
public class KeepAllRetentionPolicy : IEventRetentionPolicy
{
    public bool ShouldRetain(StoredEvent storedEvent) => true;
}
