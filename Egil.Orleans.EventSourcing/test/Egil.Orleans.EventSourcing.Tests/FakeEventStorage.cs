namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// Fake implementation of IEventStorage for testing.
/// </summary>
public class FakeEventStorage : IEventStorage
{
    public Dictionary<GrainId, List<object>> SavedEvents { get; } = new();

    public Dictionary<GrainId, object> SavedProjection { get; } = new();

    public ValueTask<TProjection?> LoadProjectionAsync<TProjection>(GrainId grainId, CancellationToken cancellationToken = default)
        where TProjection : class
    {
        // Return null to simulate empty storage for now
        return ValueTask.FromResult<TProjection?>(SavedProjection.ContainsKey(grainId) ? (TProjection)SavedProjection[grainId] as TProjection : null);
    }

    public async IAsyncEnumerable<TEvent> LoadEventsAsync<TEvent>(GrainId grainId, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        if (SavedEvents.ContainsKey(grainId))
        {
            foreach (var @event in SavedEvents[grainId])
            {
                yield return (TEvent)@event;
            }
        }
    }

    public ValueTask SaveAsync<TEvent, TProjection>(GrainId grainId, IEnumerable<TEvent> events, TProjection projection, CancellationToken cancellationToken = default)
        where TEvent : class
        where TProjection : class
    {
        // Capture saved events and projection for verification in tests
        SavedEvents[grainId] = events.OfType<object>().ToList();

        SavedProjection[grainId] = projection;

        return ValueTask.CompletedTask;
    }
}
