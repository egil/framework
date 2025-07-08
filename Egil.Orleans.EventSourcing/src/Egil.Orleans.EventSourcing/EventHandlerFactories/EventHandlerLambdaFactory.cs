using Egil.Orleans.EventSourcing.EventHandlers;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection> : IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory;
    private readonly TEventGrain eventGrain;
    private IEventHandler<TProjection>? handler;

    public EventHandlerLambdaFactory(Func<TEvent, TProjection, TProjection> handlerLambda, TEventGrain eventGrain)
        : this(_ => handlerLambda, eventGrain)
    {
    }

    public EventHandlerLambdaFactory(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory, TEventGrain eventGrain)
    {
        this.handlerFactory = handlerFactory;
        this.eventGrain = eventGrain;
    }

    public IEventHandler<TProjection> Create()
    {
        handler ??= new EventHandlerFunctionWrapper<TEvent, TProjection>(handlerFactory(eventGrain));
        return handler;
    }
}
