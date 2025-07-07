using Egil.Orleans.EventSourcing.Internal;

namespace Egil.Orleans.EventSourcing;

internal interface IEventPartition<TEventGrain, TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPartition<TEventGrain, TProjection>? TryCast<TEvent>(TEvent @event)
        where TEvent : notnull;

    IEventHandlerFactory<TEventGrain, TProjection>[] Handlers { get; }

    IEventPublisherFactory<TEventGrain, TProjection>[] Publishers { get; }
}

internal interface IEventPartition<TEventGrain, TEvent, TProjection> : IEventPartition<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    EventPartitionRetention<TEvent> Retention { get; }
}
