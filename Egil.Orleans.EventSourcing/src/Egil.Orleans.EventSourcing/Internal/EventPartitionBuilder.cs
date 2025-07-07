namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPartitionBuilder<TEventGrain, TProjection> : IEventPartitionBuilder<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventPartitionConfigurator<TEventGrain, TProjection>> configurators = [];

    public IEventPartitionConfigurator<TEventGrain, TEvent, TProjection> AddPartition<TEvent>() where TEvent : notnull
    {
        var configurator = new EventPartitionConfigurator<TEventGrain, TEvent, TProjection>();
        configurators.Add(configurator);
        return configurator;
    }

    internal IEventPartition<TEventGrain, TProjection>[] Build()
    {
        return configurators
            .Select(c => c.Build())
            .ToArray();
    }
}