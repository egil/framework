namespace Egil.Orleans.EventSourcing;

internal interface IEventStreamConfigurator<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    string StreamName { get; }

    IEventStream<TProjection> Build();
}
