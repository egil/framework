using System.Collections.Concurrent;

namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Registry that stores event handlers configured for different grain types.
/// </summary>
internal static class EventHandlerRegistry
{
    private static readonly ConcurrentDictionary<Type, IGrainEventConfiguration> Configurations = new();

    /// <summary>
    /// Registers the event configuration for a specific grain type.
    /// </summary>
    public static void RegisterConfiguration<TGrain>(IGrainEventConfiguration configuration)
    {
        Configurations[typeof(TGrain)] = configuration;
    }

    /// <summary>
    /// Gets the event configuration for a specific grain type.
    /// </summary>
    public static IGrainEventConfiguration? GetConfiguration<TGrain>()
    {
        return Configurations.TryGetValue(typeof(TGrain), out var config) ? config : null;
    }

    /// <summary>
    /// Gets the event configuration for a specific grain type.
    /// </summary>
    public static IGrainEventConfiguration? GetConfiguration(Type grainType)
    {
        return Configurations.TryGetValue(grainType, out var config) ? config : null;
    }
}

/// <summary>
/// Interface for grain event configuration.
/// </summary>
internal interface IGrainEventConfiguration
{
    /// <summary>
    /// Processes events through the configured handlers and returns the updated projection.
    /// </summary>
    ValueTask<object> ProcessEventsAsync(
        IEnumerable<object> events,
        object currentProjection,
        IEventGrainContext context);
}

/// <summary>
/// Implementation of grain event configuration that manages partitions and handlers.
/// </summary>
internal class GrainEventConfiguration<TEventBase, TProjection> : IGrainEventConfiguration
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly List<IEventPartition<TEventBase, TProjection>> partitions = new();

    /// <summary>
    /// Adds a partition to the configuration.
    /// </summary>
    public void AddPartition<TEvent>(IEventPartition<TEventBase, TProjection> partition)
        where TEvent : notnull, TEventBase
    {
        partitions.Add(partition);
    }

    /// <summary>
    /// Processes events through all configured partitions.
    /// </summary>
    public ValueTask<object> ProcessEventsAsync(
        IEnumerable<object> events,
        object currentProjection,
        IEventGrainContext context)
    {
        if (currentProjection is not TProjection typedProjection)
        {
            throw new InvalidOperationException($"Expected projection of type {typeof(TProjection).Name}, but received {currentProjection.GetType().Name}");
        }

        var result = typedProjection;

        foreach (var @event in events)
        {
            if (@event is TEventBase typedEvent)
            {
                foreach (var partition in partitions)
                {
                    if (partition.CanHandle(@event))
                    {
                        result = partition.HandleEvent(typedEvent, result, context);
                    }
                }
            }
        }

        return ValueTask.FromResult((object)result);
    }
}

/// <summary>
/// Interface for an event partition that can handle specific event types.
/// </summary>
internal interface IEventPartition<TEventBase, TProjection>
    where TProjection : class, IEventProjection<TProjection>
{
    /// <summary>
    /// Determines if this partition can handle the given event.
    /// </summary>
    bool CanHandle(object @event);

    /// <summary>
    /// Handles the event and returns the updated projection.
    /// </summary>
    TProjection HandleEvent(TEventBase @event, TProjection projection, IEventGrainContext context);
}

/// <summary>
/// Interface for a typed event partition that can add typed handlers.
/// </summary>
internal interface ITypedEventPartition<TEvent, TProjection>
    where TProjection : class, IEventProjection<TProjection>
{
    /// <summary>
    /// Adds a handler for the event type.
    /// </summary>
    void AddHandler(Func<TEvent, TProjection, TProjection> handler);
    void AddHandler(Func<TEvent, TProjection, IEventGrainContext, TProjection> handler);
}

/// <summary>
/// Implementation of an event partition for a specific event type.
/// </summary>
internal class EventPartition<TEvent, TEventBase, TProjection> : IEventPartition<TEventBase, TProjection>, ITypedEventPartition<TEvent, TProjection>
    where TEvent : notnull, TEventBase
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly List<Func<TEvent, TProjection, IEventGrainContext, TProjection>> handlers = new();

    /// <summary>
    /// Adds a handler for the event type.
    /// </summary>
    public void AddHandler(Func<TEvent, TProjection, TProjection> handler)
    {
        handlers.Add((e, p, _) => handler(e, p));
    }

    public void AddHandler(Func<TEvent, TProjection, IEventGrainContext, TProjection> handler)
    {
        handlers.Add(handler);
    }

    /// <summary>
    /// Determines if this partition can handle the given event.
    /// </summary>
    public bool CanHandle(object @event)
    {
        return @event is TEvent;
    }

    /// <summary>
    /// Handles the event through all registered handlers.
    /// </summary>
    public TProjection HandleEvent(TEventBase @event, TProjection projection, IEventGrainContext context)
    {
        if (@event is not TEvent typedEvent)
        {
            return projection;
        }

        var result = projection;
        foreach (var handler in handlers)
        {
            result = handler(typedEvent, result, context);
        }

        return result;
    }
}
