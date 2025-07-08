using Egil.Orleans.EventSourcing.EventHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerServiceProviderFactory<TEventGrain, TEvent, TProjection, TEventHandler>(IServiceProvider serviceProvider) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
    where TEventHandler : IEventHandler<TEvent, TProjection>
{
    private IEventHandler<TProjection>? handler;

    public IEventHandler<TProjection> Create()
    {
        handler ??= new EventHandlerWrapper<TEvent, TProjection>(serviceProvider.GetRequiredService<TEventHandler>());
        return handler;
    }
}

