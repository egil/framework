namespace Egil.Orleans.EventSourcing.EventReactors;

public interface IEventReactor<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask ReactAsync<TRequestedEvent>(IEnumerable<TRequestedEvent> @event, TProjection projection, IEventReactContext context) where TRequestedEvent : notnull;

    bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull;

    string? Identifier { get; }
}