namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Real implementation of IEventPartitonBuilder for partition configuration.
/// </summary>
internal class EventPartitionBuilder<TEventGrain, TEventBase, TProjection> : IEventPartitonBuilder<TEventGrain, TEventBase, TProjection>
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly GrainEventConfiguration<TEventBase, TProjection> configuration;

    public EventPartitionBuilder(GrainEventConfiguration<TEventBase, TProjection> configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Creates a new partition for the specified event type.
    /// </summary>
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> AddPartition<TEvent>()
        where TEvent : notnull, TEventBase
    {
        var partition = new EventPartition<TEvent, TEventBase, TProjection>();
        configuration.AddPartition<TEvent>(partition);

        return new EventPartitionConfigurator<TEventGrain, TEvent, TProjection>(partition);
    }
}

/// <summary>
/// Real implementation of IEventPartitionConfigurator for handler configuration.
/// </summary>
internal class EventPartitionConfigurator<TEventGrain, TEvent, TProjection> : IEventPartitionConfigurator<TEventGrain, TEvent, TProjection>
    where TEvent : notnull
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly ITypedEventPartition<TEvent, TProjection> partition;

    public EventPartitionConfigurator(ITypedEventPartition<TEvent, TProjection> partition)
    {
        this.partition = partition ?? throw new ArgumentNullException(nameof(partition));
    }

    #region Retention Configuration (minimal implementation for now)
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> KeepUntilProcessed() => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> KeepLast(int count) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> KeepUntil(TimeSpan time, Func<TEvent, DateTimeOffset> eventTimestampSelector) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> KeepDistinct<TKey>(Func<TEvent, TKey> eventKeySelector) where TKey : notnull, IEquatable<TKey> => this;
    #endregion

    #region Handle methods - Simple implementations for TDD
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, TProjection>> handlerFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEventGrain, Func<TEvent, TProjection, ValueTask<TProjection>>> handlerFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>>> handlerFactory) => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEvent, TProjection, TProjection> handler)
    {
        partition.AddHandler(handler);
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEvent, TProjection, IEventGrainContext, TProjection> handler)
    {
        partition.AddHandler(handler);
        return this;
    }
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEvent, TProjection, ValueTask<TProjection>> handler) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle(Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>> handler) => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, TProjection>> handlerFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, IEventGrainContext, TProjection>> handlerFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, ValueTask<TProjection>>> handlerFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, IEventGrainContext, ValueTask<TProjection>>> handlerFactory) where TSpecificEvent : TEvent => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TSpecificEvent, TProjection, TProjection> handler) where TSpecificEvent : TEvent
    {
        // This is the key method - it adds a typed handler for the specific event type
        if (partition is EventPartition<TSpecificEvent, TEvent, TProjection> typedPartition)
        {
            typedPartition.AddHandler(handler);
        }
        else if (typeof(TSpecificEvent) == typeof(TEvent))
        {
            partition.AddHandler((Func<TEvent, TProjection, TProjection>)(object)handler);
        }
        return this;
    }

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TSpecificEvent, TProjection, IEventGrainContext, TProjection> handler) where TSpecificEvent : TEvent
    {
        if (partition is EventPartition<TSpecificEvent, TEvent, TProjection> typedPartition)
        {
            typedPartition.AddHandler((e, p, ctx) => handler(e, p, ctx));
        }
        else if (typeof(TSpecificEvent) == typeof(TEvent))
        {
            partition.AddHandler((Func<TEvent, TProjection, IEventGrainContext, TProjection>)(object)handler);
        }
        return this;
    }
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TSpecificEvent, TProjection, ValueTask<TProjection>> handler) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TSpecificEvent>(Func<TSpecificEvent, TProjection, IEventGrainContext, ValueTask<TProjection>> handler) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TEventHandler>() where TEventHandler : class, IEventHandler<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TEventHandler>(TEventHandler handler) where TEventHandler : class, IEventHandler<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TEventHandler>(Func<TEventGrain, TEventHandler> handlerFactory) where TEventHandler : class, IEventHandler<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Handle<TEventHandler>(Func<IServiceProvider, TEventHandler> handlerFactory) where TEventHandler : class, IEventHandler<TEvent, TProjection> => this;
    #endregion

    #region Publish methods - Minimal implementations for now
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<TEvent, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<TEvent, TProjection, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEvent, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEvent, TProjection, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEvent, TProjection, IEventGrainContext, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<TSpecificEvent, TProjection, IEventGrainContext, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TSpecificEvent, ValueTask> publisher) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TSpecificEvent, TProjection, ValueTask> publisher) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TSpecificEvent, TProjection, IEventGrainContext, ValueTask> publisher) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEvent>, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, IEventGrainContext, ValueTask>> publisherFactory) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<IEnumerable<TEvent>, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<IEnumerable<TEvent>, TProjection, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish(Func<IEnumerable<TEvent>, TProjection, IEventGrainContext, ValueTask> publisher) => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<IEnumerable<TEvent>, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<TEventGrain, Func<IEnumerable<TEvent>, TProjection, IEventGrainContext, ValueTask>> publisherFactory) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<IEnumerable<TEvent>, ValueTask> publisher) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<IEnumerable<TEvent>, TProjection, ValueTask> publisher) where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent>(Func<IEnumerable<TEvent>, TProjection, IEventGrainContext, ValueTask> publisher) where TSpecificEvent : TEvent => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent, TEventPublisher>() where TEventPublisher : class, IEventPublisher<TSpecificEvent, TProjection> where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent, TEventPublisher>(TEventPublisher handler) where TEventPublisher : class, IEventPublisher<TSpecificEvent, TProjection> where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent, TEventPublisher>(Func<TEventGrain, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TSpecificEvent, TProjection> where TSpecificEvent : TEvent => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TSpecificEvent, TEventPublisher>(Func<IServiceProvider, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TSpecificEvent, TProjection> where TSpecificEvent : TEvent => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TEventPublisher>() where TEventPublisher : class, IEventPublisher<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TEventPublisher>(TEventPublisher handler) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TEventPublisher>(Func<TEventGrain, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> => this;
    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> Publish<TEventPublisher>(Func<IServiceProvider, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> StreamPublish(string streamProviderName, Action<IEventStreamPublicationConfigurator<TEventGrain, TEvent, TProjection>> publishConfigurator) => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> StreamPublish(string streamProviderName, string streamNamespace, Action<IEventStreamNamespacePublicationConfigurator<TEventGrain, TEvent, TProjection>> publishConfigurator) => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> StreamPublish<TSpecificEvent>(string streamProviderName, Action<IEventStreamPublicationConfigurator<TEventGrain, TSpecificEvent, TProjection>> publishConfigurator) where TSpecificEvent : TEvent => this;

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> StreamPublish<TSpecificEvent>(string streamProviderName, string streamNamespace, Action<IEventStreamNamespacePublicationConfigurator<TEventGrain, TSpecificEvent, TProjection>> publishConfigurator) where TSpecificEvent : TEvent => this;
    #endregion
}
