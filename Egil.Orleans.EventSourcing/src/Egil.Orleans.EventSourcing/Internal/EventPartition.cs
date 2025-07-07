namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartition<TEventGrain, TEvent, TProjection> : IEventPartition<TEventGrain, TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required IEventHandlerFactory<TEventGrain, TProjection>[] Handlers { get; init; }

    public required IEventPublisherFactory<TEventGrain, TProjection>[] Publishers { get; init; }

    public required EventPartitionRetention<TEvent> Retention { get; init; }

    public IEventPartition<TEventGrain, TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return this;
        }

        return null;
    }
}
