using Egil.Orleans.EventSourcing.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerServiceProviderFactory<TEventGrain, TEvent, TProjection, TEventHandler>(IServiceProvider serviceProvider) : IEventHandlerFactory<TProjection>
    where TEventGrain : IGrainBase
    where TEvent : notnull
    where TProjection : notnull
    where TEventHandler : IEventHandler<TEvent, TProjection>
{
    private IEventHandler<TProjection>? handler;

    public IEventHandler<TProjection> Create()
    {
        handler ??= new EventHandlerWrapper<TEvent, TProjection>(serviceProvider.GetRequiredService<TEventHandler>());
        return handler;
    }
}

