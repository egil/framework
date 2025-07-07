namespace Egil.Orleans.EventSourcing.Internal.EventReactors;

internal interface IEventReactor<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventReactor<TEvent, TProjection>? TryCast<TEvent>(TEvent @event)
        where TEvent : notnull;
}
