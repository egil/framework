using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing.Configurations;

internal interface IEventStreamConfigurator<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    string StreamName { get; }

    IEventStream<TProjection> Build();
}
