using Egil.Orleans.EventSourcing.Internal.EventReactors;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internal.EventReactorFactories;

internal interface IEventReactorFactory<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventReactor<TProjection> Create(IGrainBase grain, IServiceProvider serviceProvider);
}
