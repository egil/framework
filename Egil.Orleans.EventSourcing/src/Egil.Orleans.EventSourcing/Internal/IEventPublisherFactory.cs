namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventPublisherFactory<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPublisher<TProjection> Create(TEventGrain grain, IServiceProvider serviceProvider);
}
