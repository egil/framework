using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventPublisherFactory<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPublisher<TProjection> Create(IGrainBase grain, IServiceProvider serviceProvider);
}
