namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventPublisherFactory<TEventGrain>
{
    IEventPublisher Create(TEventGrain grain, IServiceProvider serviceProvider);
}
