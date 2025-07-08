using Egil.Orleans.EventSourcing.Internals.EventReactors;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internals.EventReactorFactories;

internal class EventReactorFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> publisherFactory) : IEventReactorFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventReactor<TProjection> Create(IGrainBase grain, IServiceProvider serviceProvider)
        => EventReactorWrapper<TEvent, TProjection>.Create(publisherFactory((TEventGrain)grain));
}
