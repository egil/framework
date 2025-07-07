using Egil.Orleans.EventSourcing.Internal;

namespace Egil.Orleans.EventSourcing;

internal interface IEventPartition<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPartition<TProjection>? TryCast<TEvent>(TEvent @event) where TEvent : notnull;

    IEventHandlerFactory<TProjection>[] Handlers { get; }

    IEventPublisherFactory<TProjection>[] Publishers { get; }
}

internal interface IEventPartition<TEvent, TProjection> : IEventPartition<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    EventPartitionRetention<TEvent> Retention { get; }
}
