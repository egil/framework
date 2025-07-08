using Egil.Orleans.EventSourcing.EventReactors;

namespace Egil.Orleans.EventSourcing.EventReactorFactories;

public interface IEventReactorFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventReactor<TProjection> Create();
}
