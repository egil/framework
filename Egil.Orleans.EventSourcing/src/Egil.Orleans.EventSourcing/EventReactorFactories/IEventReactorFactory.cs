using Egil.Orleans.EventSourcing.EventReactors;
using Orleans;

namespace Egil.Orleans.EventSourcing.EventReactorFactories;

public interface IEventReactorFactory<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventReactor<TProjection> Create();
}
