namespace Egil.Orleans.EventSourcing;

internal interface IEventStreamConfigurator<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventStream<TProjection> Build();
}
