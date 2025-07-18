namespace Egil.Orleans.EventSourcing.Reactors;

internal interface IEventReactor<TProjection> where TProjection : notnull
{
    ValueTask ReactAsync(IEnumerable<IEventEntry> eventEntries, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);

    bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull;

    string Id { get; }
}