namespace Egil.Orleans.EventSourcing;

public interface IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : class, IEventProjection<TProjection>
{
    /// <summary>
    /// Partition only keeps the <typeparamref name="TEventBase"/> until all handlers and publishers have processed it successfully.
    /// </summary>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepUntilProcessed();

    /// <summary>
    /// Partition keeps the latest <paramref name="count"/> <typeparamref name="TEventBase"/> events.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the partition.
    /// </remarks>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepLast(int count);

    /// <summary>
    /// Partition keeps the latest <paramref name="time"/> <typeparamref name="TEventBase"/> events based on the <paramref name="eventTimestampSelector"/> compared to the current time.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the partition.
    /// </remarks>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepUntil(TimeSpan time, Func<TEventBase, DateTimeOffset> eventTimestampSelector);

    /// <summary>
    /// Partition keeps the distinct <typeparamref name="TEventBase"/> events based on the <paramref name="eventKeySelector"/>.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the partition.
    /// </remarks>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct<TKey>(Func<TEventBase, TKey> eventKeySelector) where TKey : notnull, IEquatable<TKey>;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, Func<TEventBase, TProjection, TProjection>> handlerFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, Func<TEventBase, TProjection, IEventGrainContext, TProjection>> handlerFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, Func<TEventBase, TProjection, ValueTask<TProjection>>> handlerFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, Func<TEventBase, TProjection, IEventGrainContext, ValueTask<TProjection>>> handlerFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, TProjection> handler);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, IEventGrainContext, TProjection> handler);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, ValueTask<TProjection>> handler);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, IEventGrainContext, ValueTask<TProjection>> handler);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, TProjection>> handlerFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, ValueTask<TProjection>>> handlerFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>>> handlerFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, IEventGrainContext, TProjection> handler) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, ValueTask<TProjection>> handler) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>> handler) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : class, IEventHandler<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>(TEventHandler handler) where TEventHandler : class, IEventHandler<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>(Func<TEventGrain, TEventHandler> handlerFactory) where TEventHandler : class, IEventHandler<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>(Func<IServiceProvider, TEventHandler> handlerFactory) where TEventHandler : class, IEventHandler<TEventBase, TProjection>;


    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<TEventBase, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<TEventBase, TProjection, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<TEventBase, TProjection, IEventGrainContext, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventBase, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventBase, TProjection, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventBase, TProjection, IEventGrainContext, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<TEvent, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, IEventGrainContext, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEvent, ValueTask> publisher) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEvent, TProjection, ValueTask> publisher) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEvent, TProjection, IEventGrainContext, ValueTask> publisher) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEventBase>, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEventBase>, TProjection, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, Func<IEnumerable<TEventBase>, TProjection, IEventGrainContext, ValueTask>> publisherFactory);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<IEnumerable<TEventBase>, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<IEnumerable<TEventBase>, TProjection, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<IEnumerable<TEventBase>, TProjection, IEventGrainContext, ValueTask> publisher);
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<IEnumerable<TEventBase>, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<IEnumerable<TEventBase>, TProjection, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, Func<IEnumerable<TEventBase>, TProjection, IEventGrainContext, ValueTask>> publisherFactory) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<IEnumerable<TEventBase>, ValueTask> publisher) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<IEnumerable<TEventBase>, TProjection, ValueTask> publisher) where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<IEnumerable<TEventBase>, TProjection, IEventGrainContext, ValueTask> publisher) where TEvent : TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent, TEventPublisher>() where TEventPublisher : class, IEventPublisher<TEvent, TProjection> where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent, TEventPublisher>(TEventPublisher handler) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent, TEventPublisher>(Func<TEventGrain, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> where TEvent : TEventBase;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent, TEventPublisher>(Func<IServiceProvider, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEvent, TProjection> where TEvent : TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEventPublisher>() where TEventPublisher : class, IEventPublisher<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEventPublisher>(TEventPublisher handler) where TEventPublisher : class, IEventPublisher<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEventPublisher>(Func<TEventGrain, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEventBase, TProjection>;
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEventPublisher>(Func<IServiceProvider, TEventPublisher> handlerFactory) where TEventPublisher : class, IEventPublisher<TEventBase, TProjection>;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> StreamPublish(string streamProviderName, Action<IEventStreamPublicationConfigurator<TEventGrain, TEventBase, TProjection>> publishConfigurator);

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> StreamPublish(string streamProviderName, string streamNamespace, Action<IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection>> publishConfigurator);

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> StreamPublish<TEvent>(string streamProviderName, Action<IEventStreamPublicationConfigurator<TEventGrain, TEvent, TProjection>> publishConfigurator) where TEvent : TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> StreamPublish<TEvent>(string streamProviderName, string streamNamespace, Action<IEventStreamNamespacePublicationConfigurator<TEventGrain, TEvent, TProjection>> publishConfigurator) where TEvent : TEventBase;
}
