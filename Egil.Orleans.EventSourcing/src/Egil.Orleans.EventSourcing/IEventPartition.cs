using Egil.Orleans.EventSourcing.Internal;

namespace Egil.Orleans.EventSourcing;

internal interface IEventPartition<TEventGrain>
{
    IEventHandlerFactory<TEventGrain>[] Handlers { get; }

    IEventPublisherFactory<TEventGrain>[] Publishers { get; }
}

internal interface IEventPartition<TEventGrain, TEvent> : IEventPartition<TEventGrain>
    where TEvent : notnull
{
    EventPartitionRetention<TEvent> Retention { get; }
}
