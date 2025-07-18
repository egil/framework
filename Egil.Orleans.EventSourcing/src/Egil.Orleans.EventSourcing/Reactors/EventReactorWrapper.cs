
namespace Egil.Orleans.EventSourcing.Reactors;

internal class EventReactorWrapper<TEvent, TProjection> : IEventReactor<TEvent, TProjection>, IEventReactor<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventReactor<TEvent, TProjection> reactor;

    public string Id { get; }

    public EventReactorWrapper(IEventReactor<TEvent, TProjection> reactor, string identifier)
    {
        this.reactor = reactor;
        Id = identifier;
    }

    public ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        => reactor.ReactAsync(@event, projection, context, cancellationToken);

    public ValueTask ReactAsync(IEnumerable<IEventEntry> eventEntries, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
    {
        return ReactAsync(
            eventEntries.OfType<IEventEntry<TEvent>>().Select(x => x.Event),
            projection,
            context,
            cancellationToken);
    }

    public bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return true;
        }

        return false;
    }
}