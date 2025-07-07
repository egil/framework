using Egil.Orleans.EventSourcing.Internal.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;

internal class EventHandlerServiceProviderFactory<TEventGrain, TEvent, TProjection, TEventHandler>() : IEventHandlerFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
    where TEventHandler : IEventHandler<TEvent, TProjection>
{
    public IEventHandler<TProjection>? TryCreate<TRequestedEvent>(TRequestedEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return EventHandlerWrapper<TEvent, TProjection>.Create(serviceProvider.GetRequiredService<TEventHandler>()).TryCast(@event);
        }

        return null;
    }
}
