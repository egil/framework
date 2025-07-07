namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventHandlerFactory<TEventGrain>
{
    IEventHandler Create(TEventGrain grain, IServiceProvider serviceProvider);
}
