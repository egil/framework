using Orleans;

namespace Egil.Orleans.EventSourcing;

internal interface IEventPartitionConfigurator<TEventGrain> where TEventGrain : IGrain
{
    IEventPartition<TEventGrain, TEventBase, TProjection> Build();
}

public interface IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventGrain : IGrain
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

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase;

    IEventPartitionConfigurator<TEventGrain, TEventBase, TProjection> Publish<TEvent>(Func<TEventGrain, IEventPublisher<TEvent, TProjection>> publisherFactory)
        where TEvent : notnull, TEventBase;
}
