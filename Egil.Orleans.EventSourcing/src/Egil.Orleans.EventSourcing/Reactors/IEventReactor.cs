namespace Egil.Orleans.EventSourcing.Reactors;

internal interface IEventReactor<TProjection> where TProjection : notnull
{
    ValueTask ReactAsync<TRequestedEvent>(IEnumerable<TRequestedEvent> @event, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) where TRequestedEvent : notnull;

    bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull;

    string Id { get; }
}