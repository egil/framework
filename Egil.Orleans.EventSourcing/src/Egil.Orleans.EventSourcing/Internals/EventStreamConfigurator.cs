using Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internal.EventReactorFactories;
using Egil.Orleans.EventSourcing.Internals.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internals.EventReactorFactories;
using Orleans;

namespace Egil.Orleans.EventSourcing.Internals;

internal partial class EventStreamConfigurator<TEventGrain, TEventBase, TProjection> : IEventStreamConfigurator<TEventGrain, TEventBase, TProjection>, IEventStreamConfigurator<TProjection>
    where TEventGrain : IGrainBase
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private bool untilProcessed;
    private int? keepCount;
    private TimeSpan? keepAge;
    private Func<TEventBase, DateTimeOffset>? timestampSelector;
    private Func<TEventBase, string>? keySelector;
    private List<IEventHandlerFactory<TProjection>> handlers = [];
    private List<IEventReactorFactory<TProjection>> publishers = [];
    private string streamName;

    public EventStreamConfigurator(string streamName) => this.streamName = streamName;

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

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct(Func<TEventBase, string> eventKeySelector)
    {
        ArgumentNullException.ThrowIfNull(eventKeySelector);
        keySelector = eventKeySelector;
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, IEventHandler<TEventBase, TProjection>> handlerFactory)
        => Handle<TEventBase>(handlerFactory);

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerFactory<TEventGrain, TEvent, TProjection>(handlerFactory));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, TProjection> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEventBase, TProjection>(handler));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory)
        where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handlerFactory));
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : IEventHandler<TEventBase, TProjection>
    {
        handlers.Add(new EventHandlerServiceProviderFactory<TEventGrain, TEventBase, TProjection, TEventHandler>());
        return this;
    }

    public IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handler));
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
        if (untilProcessed && (keepCount.HasValue || keepAge.HasValue || keySelector != null))
        {
            throw new InvalidOperationException("Cannot combine KeepUntilProcessed with other keep settings.");
        }

        return new EventStream<TEventBase, TProjection>
        {
            Name = streamName,
            Handlers = handlers.ToArray(),
            Publishers = publishers.ToArray(),
            Retention = new EventStreamRetention<TEventBase>
            {
                UntilProcessed = untilProcessed,
                Count = keepCount,
                MaxAge = keepAge,
                TimestampSelector = timestampSelector,
                KeySelector = keySelector
            },
        };
    }
}