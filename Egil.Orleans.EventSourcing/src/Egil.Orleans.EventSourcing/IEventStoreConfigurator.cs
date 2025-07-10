using Orleans;

namespace Egil.Orleans.EventSourcing.EventStores;

public interface IEventStoreConfigurator<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull
{
    /// <summary>
    /// Creates a new stream in the grains <see cref="IEventStore"/> that by default keeps its events indefinitely.
    /// Override this by calling one of the "keep" methods.
    /// </summary>
    /// <param name="streamName">
    /// The unique name of the stream. Two streams in the same event store cannot share a name.
    /// If none is specified, then the full name of the <typeparamref name="TEvent"/> is used,
    /// or if <typeparamref name="TEvent"/> has the <see cref="AliasAttribute"/>, the alias' value is used.
    /// </param>
    /// <typeparam name="TEvent">
    /// The (base) type for the event's that can be stored in the stream.
    /// Types that derive from <typeparamref name="TEvent"/> will also be stored in this stream.
    /// </typeparam>
    /// <returns></returns>
    IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>(string? streamName = null)
        where TEvent : notnull;
}
