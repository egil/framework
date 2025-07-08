using Egil.Orleans.EventSourcing.Internals.EventReactors;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internals.EventReactorFactories;

internal interface IEventReactorFactory<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventReactor<TProjection> Create(IGrainBase grain, IServiceProvider serviceProvider);
}
