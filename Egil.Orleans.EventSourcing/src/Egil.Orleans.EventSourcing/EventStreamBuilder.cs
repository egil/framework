using Orleans;

namespace Egil.Orleans.EventSourcing;

internal class EventStreamBuilder<TEventGrain, TProjection>(TEventGrain eventGrain, IServiceProvider grainServiceProvider, IEventStore eventStore) : IEventStreamBuilder<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventStreamConfigurator<TProjection>> configurators = [];

    public IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>(string? streamName = null) where TEvent : notnull
    {
        streamName = string.IsNullOrWhiteSpace(streamName)
            ? GetAliasOrFullName(typeof(TEvent)) ?? throw new InvalidOperationException($"Cannot determine stream name for event type {typeof(TEvent).FullName}.")
            : streamName;

        var configurator = new EventStreamConfigurator<TEventGrain, TEvent, TProjection>(eventGrain, grainServiceProvider, eventStore, streamName);
        configurators.Add(configurator);
        return configurator;
    }

    internal IEventStream<TProjection>[] Build()
    {
        if (configurators.Count == 0)
        {
            throw new Exception("No event streams configured. Please add at least one stream using AddStream<TEvent>().");
        }

        return configurators
            .Select(c => c.Build())
            .ToArray();
    }

    private static string GetAliasOrFullName(Type type)
    {
        var aliasAttribute = type.GetCustomAttributes(typeof(AliasAttribute), false).FirstOrDefault() as AliasAttribute;
        return aliasAttribute?.Alias
            ?? type.FullName
            ?? type.Name;
    }
}