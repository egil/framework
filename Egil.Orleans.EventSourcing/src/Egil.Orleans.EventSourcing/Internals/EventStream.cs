using Egil.Orleans.EventSourcing.Internals.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internals.EventReactorFactories;

namespace Egil.Orleans.EventSourcing.Internals;

internal class EventStream<TEvent, TProjection> : IEventStream<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required string Name { get; init; }

    public required IEventHandlerFactory<TProjection>[] Handlers { get; init; }

    public required IEventReactorFactory<TProjection>[] Publishers { get; init; }

    public required EventStreamRetention<TEvent> Retention { get; init; }

    public bool Matches<TRequestedEvent>(TRequestedEvent? @event) where TRequestedEvent : notnull
    {
        if (@event is null)
        {
            return Matches<TRequestedEvent>();
        }

        if (@event is TEvent)
        {
            return true;
        }

        return false;
    }

    private bool Matches<TRequestedEvent>() where TRequestedEvent : notnull
    {
        if (typeof(TRequestedEvent).IsAssignableTo(typeof(TEvent)))
        {
            return true;
        }

        return false;
    }
}
