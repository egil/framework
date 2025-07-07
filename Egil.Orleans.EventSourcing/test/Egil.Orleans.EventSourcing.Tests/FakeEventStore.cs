
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// Fake implementation of IEventStorage for testing.
/// </summary>
public class FakeEventStore : IEventStore
{
    private readonly ConcurrentDictionary<(GrainId, string), (List<object> Events, object Projection)> store = new();

    public ValueTask<TProjection?> LoadProjectionAsync<TProjection>(string streamName, GrainId grainId, CancellationToken cancellationToken = default) where TProjection : notnull
    {
        if (store.TryGetValue((grainId, streamName), out var entry))
        {
            return ValueTask.FromResult((TProjection?)entry.Projection);
        }

        return ValueTask.FromResult<TProjection?>(default);
    }

    public async IAsyncEnumerable<TEvent> LoadEventsAsync<TEvent>(string streamName, GrainId grainId, [EnumeratorCancellation] CancellationToken cancellationToken = default) where TEvent : notnull
    {
        await Task.Yield();

        if (store.TryGetValue((grainId, streamName), out var entry))
        {
            foreach (var @event in entry.Events.OfType<TEvent>())
            {
                yield return @event;
            }
        }
    }

    public ValueTask SaveAsync<TEvent, TProjection>(string streamName, GrainId grainId, IEnumerable<TEvent> events, TProjection projection, CancellationToken cancellationToken = default)
        where TEvent : notnull
        where TProjection : notnull
    {
        var key = (grainId, streamName);
        var entry = store.GetOrAdd(key, _ => ([.. events], projection));
        foreach (var @event in events)
        {
            entry.Events.Add(@event);
        }
        entry.Projection = projection;
        return ValueTask.CompletedTask;
    }
}
