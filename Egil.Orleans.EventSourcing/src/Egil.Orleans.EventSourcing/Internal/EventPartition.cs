namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartition<TEvent, TProjection> : IEventPartition<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required IEventHandlerFactory<TProjection>[] Handlers { get; init; }

    public required IEventPublisherFactory<TProjection>[] Publishers { get; init; }

    public required EventPartitionRetention<TEvent> Retention { get; init; }

    public IEventPartition<TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return this;
        }

        return null;
    }
}
