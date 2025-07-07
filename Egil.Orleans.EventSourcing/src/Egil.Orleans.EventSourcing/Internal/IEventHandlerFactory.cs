using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventHandlerFactory<TEventGrain, TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    IEventHandler<TEvent, TProjection>? TryCreate<TEvent>(TEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TEvent: notnull;
}
