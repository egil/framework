namespace Egil.Orleans.EventSourcing;

public interface IEventStreamBuilder<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    /// <summary>
    /// Creates a new stream in the grains <see cref="IEventStore"/> that by default keeps its events indefinitely.
    /// Override this by calling one of the "keep" methods.
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns></returns>
    IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>()
        where TEvent : notnull;
}
