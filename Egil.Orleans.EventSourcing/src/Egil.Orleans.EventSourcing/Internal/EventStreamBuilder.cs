using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventStreamBuilder<TEventGrain, TProjection> : IEventStreamBuilder<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventStreamConfigurator<TProjection>> configurators = [];

    public IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>() where TEvent : notnull
    {
        var configurator = new EventStreamConfigurator<TEventGrain, TEvent, TProjection>();
        configurators.Add(configurator);
        return configurator;
    }

    internal IEventStream<TProjection>[] Build()
    {
        return configurators
            .Select(c => c.Build())
            .ToArray();
    }
}