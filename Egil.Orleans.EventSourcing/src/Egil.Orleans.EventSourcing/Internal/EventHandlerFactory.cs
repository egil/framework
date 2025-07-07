using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler<TRequestedEvent, TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent && grain is TEventGrain eventGrain)
        {
            return EventHandlerWrapper<TEvent, TProjection>
                .Create(handlerFactory(eventGrain))
                .TryCast(@event);
        }

        return null;
    }
}

internal class EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection> : IEventHandlerFactory<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory;

    public EventHandlerLambdaFactory(Func<TEvent, TProjection, TProjection> handlerLambda)
        : this(_ => handlerLambda)
    {
    }

    public EventHandlerLambdaFactory(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory)
    {
        this.handlerFactory = handlerFactory;
    }

    public IEventHandler<TRequestedEvent, TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent && grain is TEventGrain eventGrain)
        {
            return EventHandlerWrapper<TEvent, TProjection>
                .Create(handlerFactory(eventGrain))
                .TryCast(@event);
        }

        return null;
    }
}

internal class EventHandlerServiceProviderFactory<TEventGrain, TEvent, TProjection, TEventHandler>() : IEventHandlerFactory<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
    where TEventHandler : IEventHandler<TEvent, TProjection>
{
    public IEventHandler<TRequestedEvent, TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return EventHandlerWrapper<TEvent, TProjection>
                .Create(serviceProvider.GetRequiredService<TEventHandler>())
                .TryCast(@event);
        }

        return null;
    }
}

internal class EventHandlerSingletonFactory<TEventGrain, TEvent, TProjection>(IEventHandler<TEvent, TProjection> handler) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventHandler<TProjection> handler = EventHandlerWrapper<TEvent, TProjection>.Create(handler);

    public IEventHandler<TProjection> Create(TEventGrain grain, IServiceProvider serviceProvider)
        => handler;

    public IEventHandler<TRequestedEvent, TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return handler.TryCast(@event);
        }

        return null;
    }
}
