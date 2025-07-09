using Egil.Orleans.EventSourcing.EventStores;
using Egil.Orleans.EventSourcing.Storage;
using Orleans;

namespace Egil.Orleans.EventSourcing;

internal class EventStreamBuilder<TEventGrain, TProjection>(TEventGrain eventGrain, IServiceProvider grainServiceProvider, IEventStore<TEventGrain, TProjection> eventStore) : IEventStoreConfigurator<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly List<IEventStreamConfigurator<TProjection>> configurators = [];

    public IEventStreamConfigurator<TEventGrain, TEvent, TProjection> AddStream<TEvent>(string? streamName = null) where TEvent : notnull
    {
        streamName = string.IsNullOrWhiteSpace(streamName)
            ? GetAliasOrFullName(typeof(TEvent)) ?? throw new InvalidOperationException($"Cannot determine stream name for event type {typeof(TEvent).FullName}.")
            : streamName;

        if (streamName.ContainsDisallowedKeyFieldCharacters())
        {
            throw new ArgumentException($"""
                Stream name '{streamName}' contains disallowed characters.
                
                - The forward slash(/) character
                - The backslash(\) character
                - The number sign(#) character
                - The question mark(?) character
                - Control characters from U+0000 to U + 001F, including:
                    The horizontal tab(\t) character
                    The linefeed(\n) character
                    The carriage return (\r) character
                - Control characters from U + 007F to U+009F
                """,
            nameof(streamName));
        }

        if (configurators.Any(c => c.StreamName.Equals(streamName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Stream name '{streamName}' has already been used", nameof(streamName));
        }

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