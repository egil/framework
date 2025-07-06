using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventHandlerFactory<TEventGrain> where TEventGrain : IGrain
{
    IEventHandler Create(TEventGrain grain);
}
