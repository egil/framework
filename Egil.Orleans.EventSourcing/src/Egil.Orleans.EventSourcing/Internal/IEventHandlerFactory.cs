using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventHandlerFactory<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    IEventHandler<TProjection>? TryCreate<TEvent>(TEvent @event, IGrainBase grain, IServiceProvider serviceProvider) where TEvent: notnull;
}
