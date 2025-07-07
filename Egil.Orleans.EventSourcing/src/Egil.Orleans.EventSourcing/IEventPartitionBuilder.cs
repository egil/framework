using Orleans;

namespace Egil.Orleans.EventSourcing;

public interface IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    /// <summary>
    /// Creates a new partition in the grains event stream that keeps its events indefinitely. Override this by calling one of the "keep" methods.
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns></returns>
    IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> AddPartition<TEvent>()
        where TEvent : notnull, TEventBase;
}
