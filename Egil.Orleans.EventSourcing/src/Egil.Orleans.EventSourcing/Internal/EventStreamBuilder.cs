using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventStreamBuilder<TEventGrain, TProjection> : IEventStreamBuilder<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventStreamConfigurator<TProjection>> configurators = [];

    public IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>(string? streamName = null) where TEvent : notnull
    {
        streamName = string.IsNullOrWhiteSpace(streamName)
            ? GetAliasOrFullName(typeof(TEvent)) ?? throw new InvalidOperationException($"Cannot determine stream name for event type {typeof(TEvent).FullName}.")
            : streamName;

        var configurator = new EventStreamConfigurator<TEventGrain, TEvent, TProjection>(streamName);
        configurators.Add(configurator);
        return configurator;
    }

    internal IEventStream<TProjection>[] Build()
    {
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