namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventHandler<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    IEventHandler<TEvent, TProjection>? TryCast<TEvent>(TEvent @event)
        where TEvent : notnull;
}
