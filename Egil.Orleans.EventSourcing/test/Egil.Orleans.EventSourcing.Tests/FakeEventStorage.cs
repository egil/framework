
namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// Fake implementation of IEventStorage for testing.
/// </summary>
public class FakeEventStorage : IEventStore
{
    public IAsyncEnumerable<TEvent> LoadEventsAsync<TEvent>(GrainId grainId, CancellationToken cancellationToken = default) where TEvent : notnull => throw new NotImplementedException();
    public ValueTask<TProjection?> LoadProjectionAsync<TProjection>(GrainId grainId, CancellationToken cancellationToken = default) where TProjection : notnull => throw new NotImplementedException();
    public ValueTask SaveAsync<TEvent, TProjection>(GrainId grainId, IEnumerable<TEvent> events, TProjection projection, CancellationToken cancellationToken = default)
        where TEvent : notnull
        where TProjection : notnull => throw new NotImplementedException();
}
