using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory) : IEventHandlerFactory<TEventGrain>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler Create(TEventGrain grain, IServiceProvider serviceProvider)
        => handlerFactory(grain);
}

internal class EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory) : IEventHandlerFactory<TEventGrain>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler Create(TEventGrain grain, IServiceProvider serviceProvider)
        => new EventHandler<TEvent, TProjection>(handlerFactory(grain));
}

internal class EventHandlerServiceProviderFactory<TEventGrain, TEventBase, TProjection, TEventHandler>() : IEventHandlerFactory<TEventGrain>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
    where TEventHandler : IEventHandler<TEventBase, TProjection>
{
    public IEventHandler Create(TEventGrain grain, IServiceProvider serviceProvider)
        => serviceProvider.GetRequiredService<TEventHandler>();
}

internal class EventHandler<TEvent, TProjection>(Func<TEvent, TProjection, TProjection> handlerFuncReference) : IEventHandler<TEvent, TProjection>, IEventHandler
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventGrainContext context)
    {
        var result = handlerFuncReference(@event, projection);
        return ValueTask.FromResult(result);
    }
}

internal class EventHandlerSingletonFactory<TEventGrain, TEvent, TProjection>(IEventHandler<TEvent, TProjection> handler) : IEventHandlerFactory<TEventGrain>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler Create(TEventGrain grain, IServiceProvider serviceProvider)
        => handler;
}