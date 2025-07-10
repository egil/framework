namespace Egil.Orleans.EventSourcing.EventReactors;

internal class EventReactorWrapper<TEvent, TProjection> : IEventReactor<TEvent, TProjection>, IEventReactor<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventReactor<TEvent, TProjection> reactor;

    public string Identifier { get; }

    public EventReactorWrapper(IEventReactor<TEvent, TProjection> reactor, string identifier)
    {
        this.reactor = reactor;
        this.Identifier = identifier;
    }

    public ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context)
        => reactor.ReactAsync(@event, projection, context);

    public ValueTask ReactAsync<TRequestedEvent>(IEnumerable<TRequestedEvent> @event, TProjection projection, IEventReactContext context) where TRequestedEvent : notnull
    {
        if (@event is IEnumerable<TEvent> castEvent)
        {
            return ReactAsync(castEvent, projection, context);
        }

        return ValueTask.CompletedTask;
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