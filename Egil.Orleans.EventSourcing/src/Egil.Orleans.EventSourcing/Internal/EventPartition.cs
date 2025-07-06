using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartition<TEventGrain, TEvent, TProjection> : IEventPartition<TEventGrain, TEvent, TProjection>
    where TEventGrain : IGrain
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required IEventHandlerFactory<TEventGrain>[] Handlers { get; init; }

    public required IEventPublisherFactory<TEventGrain>[] Publishers { get; init; }

    public required EventPartitionRetention<TEvent> Retention { get; init; }
}
