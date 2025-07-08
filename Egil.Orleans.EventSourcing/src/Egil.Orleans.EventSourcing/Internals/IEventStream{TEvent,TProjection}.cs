namespace Egil.Orleans.EventSourcing.Internals;

internal interface IEventStream<TEvent, TProjection> : IEventStream<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    EventStreamRetention<TEvent> Retention { get; }
}
