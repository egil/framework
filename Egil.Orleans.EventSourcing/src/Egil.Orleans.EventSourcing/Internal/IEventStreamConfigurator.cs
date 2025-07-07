namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventStreamConfigurator<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventStream<TProjection> Build();
}
