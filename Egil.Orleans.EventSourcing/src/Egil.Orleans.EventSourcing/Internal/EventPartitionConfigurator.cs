namespace Egil.Orleans.EventSourcing.Internal;

internal partial class EventPartitionConfigurator<TEventGrain, TEventBase, TProjection> : IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>, IEventPartitionConfigurator<TEventGrain, TProjection>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private bool untilProcessed;
    private int? keepCount;
    private TimeSpan? keepAge;
    private Func<TEventBase, DateTimeOffset>? timestampSelector;
    private Func<TEventBase, string>? keySelector;
    private List<IEventHandlerFactory<TEventGrain, TProjection>> handlers = [];
    private List<IEventPublisherFactory<TEventGrain, TProjection>> publishers = [];

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepUntilProcessed()
    {
        untilProcessed = true;
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepLast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
        keepCount = count;
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepUntil(TimeSpan time, Func<TEventBase, DateTimeOffset> eventTimestampSelector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(time.Ticks);
        keepAge = time;
        timestampSelector = eventTimestampSelector ?? throw new ArgumentNullException(nameof(eventTimestampSelector));
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct(Func<TEventBase, string> eventKeySelector)
    {
        ArgumentNullException.ThrowIfNull(eventKeySelector);
        keySelector = eventKeySelector;
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, IEventHandler<TEventBase, TProjection>> handlerFactory)
        => Handle<TEventBase>(handlerFactory);

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerFactory<TEventGrain, TEvent, TProjection>(handlerFactory));
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, IEventPublisher<TEventBase, TProjection>> publisherFactory)
        => Publish<TEventBase>(publisherFactory);

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, IEventPublisher<TEvent, TProjection>> publisherFactory)
        where TEvent : notnull, TEventBase
    {
        ArgumentNullException.ThrowIfNull(publisherFactory);
        publishers.Add(new EventPublisherFactory<TEventGrain, TEvent, TProjection>(publisherFactory));
        return this;
    }

    public IEventPartition<TEventGrain, TProjection> Build()
    {
        if (untilProcessed && (keepCount.HasValue || keepAge.HasValue || keySelector != null))
        {
            throw new InvalidOperationException("Cannot combine KeepUntilProcessed with other keep settings.");
        }

        return new EventPartition<TEventGrain, TEventBase, TProjection>
        {
            Handlers = handlers.ToArray(),
            Publishers = publishers.ToArray(),
            Retention = new EventPartitionRetention<TEventBase>
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

internal partial class EventPartitionConfigurator<TEventGrain, TEventBase, TProjection> : IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>
{
    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory)
        where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handlerFactory));
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : IEventHandler<TEventBase, TProjection>
    {
        handlers.Add(new EventHandlerServiceProviderFactory<TEventGrain, TEventBase, TProjection, TEventHandler>());
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(new EventHandlerLambdaFactory<TEventGrain, TEvent, TProjection>(handler));
        return this;
    }
}