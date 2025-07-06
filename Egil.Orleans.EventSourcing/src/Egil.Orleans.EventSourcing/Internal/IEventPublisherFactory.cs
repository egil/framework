using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventPublisherFactory<TEventGrain> where TEventGrain : IGrain
{
    IEventPublisher Create(TEventGrain grain);
}
