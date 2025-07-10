using Egil.Orleans.EventSourcing.EventReactors;

namespace Egil.Orleans.EventSourcing.EventReactorFactories;

internal interface IEventReactorFactory<TProjection>
    where TProjection : notnull
{
    IEventReactor<TProjection> Create();
}
