namespace Egil.Orleans.EventSourcing.EventReactors;

internal interface IEventReactor<TProjection> where TProjection : notnull
{
    ValueTask ReactAsync<TRequestedEvent>(IEnumerable<TRequestedEvent> @event, TProjection projection, IEventReactContext context) where TRequestedEvent : notnull;

    bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull;

    string Identifier { get; }
}