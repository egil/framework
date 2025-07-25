using Egil.Orleans.EventSourcing.Handlers;
using Orleans;

namespace Egil.Orleans.EventSourcing.Reactors;

internal class EventReactorLambdaFactory<TEventGrain, TEvent, TProjection> : IEventReactorFactory<TProjection>
    where TEventGrain : IGrainBase
    where TEvent : notnull
    where TProjection : notnull
{
    private readonly string id;
    private readonly Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, IEventReactContext, CancellationToken, ValueTask>> handlerFactory;
    private readonly TEventGrain eventGrain;
    private IEventReactor<TProjection>? reactor;

    public EventReactorLambdaFactory(string id, Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, IEventReactContext, CancellationToken, ValueTask>> handlerFactory, TEventGrain eventGrain)
    {
        this.id = id;
        this.handlerFactory = handlerFactory;
        this.eventGrain = eventGrain;
    }

    public IEventReactor<TProjection> Create()
    {
        reactor ??= new EventReactorFunctionWrapper<TEvent, TProjection>(id, handlerFactory(eventGrain));
        return reactor;
    }
}
