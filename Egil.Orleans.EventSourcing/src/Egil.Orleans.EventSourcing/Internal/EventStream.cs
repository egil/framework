using Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internal.EventReactorFactories;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventStream<TEvent, TProjection> : IEventStream<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required IEventHandlerFactory<TProjection>[] Handlers { get; init; }

    public required IEventReactorFactory<TProjection>[] Publishers { get; init; }

    public required EventStreamRetention<TEvent> Retention { get; init; }

    public IEventStream<TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return this;
        }

        return null;
    }
}
