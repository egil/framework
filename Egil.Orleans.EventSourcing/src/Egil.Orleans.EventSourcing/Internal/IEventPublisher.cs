namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventPublisher<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPublisher<TEvent, TProjection>? TryCast<TEvent>(TEvent @event)
        where TEvent : notnull;
}
