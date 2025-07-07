namespace Egil.Orleans.EventSourcing;

internal interface IEventPartitionConfigurator<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventPartition<TEventGrain, TProjection> Build();
}

public partial interface IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
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
    /// Partition keeps the <typeparamref name="TEventBase"/> events until their <paramref name="eventTimestampSelector"/> is older than <paramref name="maxAge"/>.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the partition.
    /// </remarks>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepUntil(TimeSpan maxAge, Func<TEventBase, DateTimeOffset> eventTimestampSelector);

    /// <summary>
    /// Partition keeps the distinct <typeparamref name="TEventBase"/> events based on the <paramref name="eventKeySelector"/>.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the partition.
    /// </remarks>
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct(Func<TEventBase, string> eventKeySelector);

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, IEventHandler<TEventBase, TProjection>> handlerFactory);

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish(Func<TEventGrain, IEventPublisher<TEventBase, TProjection>> publisherFactory);

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, IEventPublisher<TEvent, TProjection>> publisherFactory)
        where TEvent : notnull, TEventBase;
}

public partial interface IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>
{
    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory) where TEvent : TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : IEventHandler<TEventBase, TProjection>;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase;
}