using Egil.Orleans.EventSourcing.Internals.EventHandlers;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internals.EventHandlerFactories;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory) : IEventHandlerFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventHandler<TProjection>? handler;

    public IEventHandler<TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent && grain is TEventGrain eventGrain)
        {
            handler ??= EventHandlerWrapper<TEvent, TProjection>.Create(handlerFactory(eventGrain));
            return handler;
        }

        return null;
    }
}
