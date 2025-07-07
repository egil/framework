namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartitionBuilder<TEventGrain, TEventBase, TProjection> : IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventPartitionConfigurator<TEventGrain>> configurators = [];

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> AddPartition<TEvent>() where TEvent : notnull, TEventBase
    {
        var configurator = new EventPartitionConfigurator<TEventGrain, TEvent, TProjection>();
        configurators.Add(configurator);
        return configurator;
    }

    internal IEventPartition<TEventGrain>[] Build()
    {
        return configurators
            .Select(c => c.Build())
            .ToArray();
    }
}