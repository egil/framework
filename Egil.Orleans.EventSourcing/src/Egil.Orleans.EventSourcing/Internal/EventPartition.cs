using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartition<TEventGrain, TEvent> : IEventPartition<TEventGrain, TEvent>
    where TEvent : notnull
{
    public required IEventHandlerFactory<TEventGrain>[] Handlers { get; init; }

    public required IEventPublisherFactory<TEventGrain>[] Publishers { get; init; }

    public required EventPartitionRetention<TEvent> Retention { get; init; }
}
