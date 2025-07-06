using Egil.Orleans.EventSourcing.Internal;
using Orleans;

namespace Egil.Orleans.EventSourcing;

internal interface IEventPartition<TEventGrain, TEvent>
    where TEventGrain : IGrain
    where TEvent : notnull
{
    IEventHandlerFactory<TEventGrain>[] Handlers { get; }

    IEventPublisherFactory<TEventGrain>[] Publishers { get; }

    EventPartitionRetention<TEvent> Retention { get; }
}
