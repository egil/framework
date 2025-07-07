using Egil.Orleans.EventSourcing.Internal.EventHandlers;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;

internal class EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection> : IEventHandlerFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory;
    private IEventHandler<TProjection>? handler;

    public EventHandlerLambdaFactory(Func<TEvent, TProjection, TProjection> handlerLambda)
        : this(_ => handlerLambda)
    {
    }

    public EventHandlerLambdaFactory(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory)
    {
        this.handlerFactory = handlerFactory;
    }

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
