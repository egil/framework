using Egil.Orleans.EventSourcing.EventHandlerFactories;
using Egil.Orleans.EventSourcing.EventReactorFactories;
using Orleans;

namespace Egil.Orleans.EventSourcing;

internal partial class EventStreamConfigurator<TEventGrain, TEventBase, TProjection> : IEventStreamConfigurator<TEventGrain, TEventBase, TProjection>, IEventStreamConfigurator<TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>, IGrainBase
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly string streamName;
    private readonly TEventGrain eventGrain;
    private readonly IServiceProvider grainServiceProvider;
    private readonly IEventStore eventStore;
    private bool untilProcessed;
    private int? keepCount;
    private TimeSpan? keepAge;
    private Func<TEventBase, DateTimeOffset>? timestampSelector;
    private Func<TEventBase, string>? eventIdSelector;
    private List<IEventHandlerFactory<TEventGrain, TProjection>> handlers = [];
    private List<IEventReactorFactory<TEventGrain, TProjection>> publishers = [];

    public EventStreamConfigurator(TEventGrain eventGrain, IServiceProvider grainServiceProvider, IEventStore eventStore, string streamName)
    {
        this.eventGrain = eventGrain;
        this.grainServiceProvider = grainServiceProvider;
        this.eventStore = eventStore;
        this.streamName = streamName;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepUntilProcessed()
    {
        untilProcessed = true;
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepLast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
        keepCount = count;
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepUntil(TimeSpan time, Func<TEventBase, DateTimeOffset> eventTimestampSelector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(time.Ticks);
        keepAge = time;
        timestampSelector = eventTimestampSelector ?? throw new ArgumentNullException(nameof(eventTimestampSelector));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct(Func<TEventBase, string> eventIdSelector)
    {
        ArgumentNullException.ThrowIfNull(eventIdSelector);
        this.eventIdSelector = eventIdSelector;
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, IEventHandler<TEventBase, TProjection>> handlerFactory)
        => Handle<TEventBase>(handlerFactory);

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerFactory<TEventGrain, TEvent, TProjection>(handlerFactory, eventGrain));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, TProjection> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEventBase, TProjection>(handler, eventGrain));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory)
        where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handlerFactory, eventGrain));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : IEventHandler<TEventBase, TProjection>
    {
        handlers.Add(new EventHandlerServiceProviderFactory<TEventGrain, TEventBase, TProjection, TEventHandler>(grainServiceProvider));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handler, eventGrain));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> React(Func<TEventGrain, IEventReactor<TEventBase, TProjection>> publisherFactory)
        => React<TEventBase>(publisherFactory);

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> React<TEvent>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> publisherFactory)
        where TEvent : notnull, TEventBase
    {
        ArgumentNullException.ThrowIfNull(publisherFactory);
        publishers.Add(new EventReactorFactory<TEventGrain, TEvent, TProjection>(publisherFactory));
        return this;
    }

    public IEventStream<TProjection> Build()
    {
        if (untilProcessed && (keepCount.HasValue || keepAge.HasValue || eventIdSelector != null))
        {
            throw new InvalidOperationException("Cannot combine KeepUntilProcessed with other keep settings.");
        }

        return new EventStream<TEventGrain, TEventBase, TProjection>(
            eventGrain.GetGrainId(),
            eventStore,
            streamName,
            handlers.ToArray(),
            publishers.ToArray(),
            new EventStreamRetention<TEventBase>
            {
                UntilProcessed = untilProcessed,
                Count = keepCount,
                MaxAge = keepAge,
                TimestampSelector = timestampSelector,
                EventIdSelector = eventIdSelector
            });
    }
}