using Egil.Orleans.EventSourcing.Internal.EventHandlers;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;

internal class EventHandlerInstanceFactory<TEventGrain, TEvent, TProjection>(IEventHandler<TEvent, TProjection> handler) : IEventHandlerFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventHandler<TProjection> handler = EventHandlerWrapper<TEvent, TProjection>.Create(handler);

    public IEventHandler<TProjection> Create(TEventGrain grain, IServiceProvider serviceProvider)
        => handler;

    public IEventHandler<TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return handler.TryCast(@event);
        }

        return null;
    }
}
