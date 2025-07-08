namespace Egil.Orleans.EventSourcing.Internals;

internal interface IEventStreamConfigurator<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventStream<TProjection> Build();
}
