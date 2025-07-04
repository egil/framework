using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Builder for configuring event streams and their handlers in a fluent, strongly-typed way.
/// </summary>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <typeparam name="TEvent">The base event type for the stream</typeparam>
/// <typeparam name="TOutboxEvent">The base outbox event type</typeparam>
public sealed class EventStreamBuilder<TProjection, TEvent, TOutboxEvent>
    where TProjection : notnull
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly Dictionary<Type, EventStreamConfiguration<TEvent>> streamConfigurations = new();
    private readonly Dictionary<Type, Func<object, TProjection, EventOutbox<TOutboxEvent>?, ValueTask<TProjection>>> eventHandlers = new();
    private readonly IServiceProvider? serviceProvider;

    /// <summary>
    /// Initializes a new EventStreamBuilder.
    /// </summary>
    /// <param name="serviceProvider">Optional service provider for DI-based handlers</param>
    public EventStreamBuilder(IServiceProvider? serviceProvider = null)
    {
        this.serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Configures an event stream for a specific event type.
    /// </summary>
    public EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent> ForEvent<TSpecificEvent>()
        where TSpecificEvent : class, TEvent
    {
        return new EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent>(this);
    }

    /// <summary>
    /// Gets the configured stream configurations.
    /// </summary>
    internal IReadOnlyDictionary<Type, EventStreamConfiguration<TEvent>> GetStreamConfigurations() => streamConfigurations;

    /// <summary>
    /// Gets the configured event handlers.
    /// </summary>
    internal IReadOnlyDictionary<Type, Func<object, TProjection, EventOutbox<TOutboxEvent>?, ValueTask<TProjection>>> GetEventHandlers() => eventHandlers;

    /// <summary>
    /// Internal method to register a stream configuration and handler.
    /// </summary>
    internal void RegisterEventHandler<TSpecificEvent>(
        EventStreamConfiguration<TEvent> streamConfig,
        IEventHandler<TSpecificEvent, TProjection, TOutboxEvent> handler)
        where TSpecificEvent : class, TEvent
    {
        var eventType = typeof(TSpecificEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) => 
            handler.ApplyEventAsync((TSpecificEvent)evt, projection, outbox);
    }

    /// <summary>
    /// Internal method to register a stream configuration and delegate handler.
    /// </summary>
    internal void RegisterEventHandler<TSpecificEvent>(
        EventStreamConfiguration<TEvent> streamConfig,
        EventHandlerDelegate<TSpecificEvent, TProjection, TOutboxEvent> handlerDelegate)
        where TSpecificEvent : class, TEvent
    {
        var eventType = typeof(TSpecificEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) => 
            handlerDelegate((TSpecificEvent)evt, projection, outbox);
    }

    /// <summary>
    /// Internal method to register a stream configuration and handler delegate with outbox.
    /// </summary>
    internal void RegisterEventHandler<TEvent>(
        EventStreamConfiguration streamConfig,
        EventHandlerDelegate<TEvent, TProjection> handlerDelegate)
    {
        var eventType = typeof(TEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) => handlerDelegate((TEvent)evt, projection, outbox!);
    }

    /// <summary>
    /// Internal method to register a stream configuration and read-only handler delegate.
    /// </summary>
    internal void RegisterEventHandler<TEvent>(
        EventStreamConfiguration streamConfig,
        EventHandlerDelegateReadOnly<TEvent, TProjection> handlerDelegate)
    {
        var eventType = typeof(TEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) => handlerDelegate((TEvent)evt, projection);
    }

    /// <summary>
    /// Internal method to register a DI-based handler.
    /// </summary>
    internal void RegisterEventHandler<TEvent, THandler>(EventStreamConfiguration streamConfig)
        where THandler : class, IEventHandler<TEvent, TProjection>
    {
        if (serviceProvider == null)
            throw new InvalidOperationException("Service provider not available for DI-based handlers");

        var eventType = typeof(TEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) =>
        {
            var handler = serviceProvider.GetRequiredService<THandler>();
            return handler.ApplyEventAsync((TEvent)evt, projection, outbox!);
    /// <summary>
    /// Internal method to register a DI-based handler.
    /// </summary>
    internal void RegisterEventHandler<TSpecificEvent, THandler>(EventStreamConfiguration<TEvent> streamConfig)
        where TSpecificEvent : class, TEvent
        where THandler : class, IEventHandler<TSpecificEvent, TProjection, TOutboxEvent>
    {
        if (serviceProvider == null)
            throw new InvalidOperationException("Service provider not available for DI-based handlers");

        var eventType = typeof(TSpecificEvent);
        streamConfigurations[eventType] = streamConfig;
        eventHandlers[eventType] = (evt, projection, outbox) =>
        {
            var handler = serviceProvider.GetRequiredService<THandler>();
            return handler.ApplyEventAsync((TSpecificEvent)evt, projection, outbox);
        };
    }
}

/// <summary>
/// Fluent builder for configuring a specific event type's stream and handler.
/// </summary>
/// <typeparam name="TSpecificEvent">The specific type of events being configured</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <typeparam name="TEvent">The base event type for the stream</typeparam>
/// <typeparam name="TOutboxEvent">The base outbox event type</typeparam>
public sealed class EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent>
    where TProjection : notnull
    where TSpecificEvent : class, TEvent
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly EventStreamBuilder<TProjection, TEvent, TOutboxEvent> parent;
    private string? streamName;
    private bool enableDeduplication = false;
    private IEventRetentionPolicy retentionPolicy = EventRetentionPolicies.KeepAll();
    private Func<TSpecificEvent, bool> shouldStoreEvent = _ => true;
    private Func<TSpecificEvent, string?> getDeduplicationId = _ => null;

    internal EventStreamHandlerBuilder(EventStreamBuilder<TProjection, TEvent, TOutboxEvent> parent)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Sets the stream name for this event type.
    /// </summary>
    public EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent> InStream(string streamName)
    {
        this.streamName = streamName;
        return this;
    }

    /// <summary>
    /// Enables deduplication for this event type.
    /// </summary>
    public EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent> WithDeduplication(Func<TSpecificEvent, string?> getDeduplicationId)
    {
        this.enableDeduplication = true;
        this.getDeduplicationId = getDeduplicationId;
        return this;
    }

    /// <summary>
    /// Sets the retention policy for this event type.
    /// </summary>
    public EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent> WithRetention(IEventRetentionPolicy retentionPolicy)
    {
        this.retentionPolicy = retentionPolicy;
        return this;
    }

    /// <summary>
    /// Sets a filter for which events should be stored.
    /// </summary>
    public EventStreamHandlerBuilder<TSpecificEvent, TProjection, TEvent, TOutboxEvent> WithFilter(Func<TSpecificEvent, bool> shouldStoreEvent)
    {
        this.shouldStoreEvent = shouldStoreEvent;
        return this;
    }

    /// <summary>
    /// Completes the configuration by providing an event handler instance.
    /// </summary>
    public EventStreamBuilder<TProjection, TEvent, TOutboxEvent> HandledBy(IEventHandler<TSpecificEvent, TProjection, TOutboxEvent> handler)
    {
        if (string.IsNullOrEmpty(streamName))
            throw new InvalidOperationException($"Stream name must be set for event type {typeof(TSpecificEvent).Name}");

        var config = CreateStreamConfiguration();
        parent.RegisterEventHandler(config, handler);
        return parent;
    }

    /// <summary>
    /// Completes the configuration by providing an event handler delegate.
    /// </summary>
    public EventStreamBuilder<TProjection, TEvent, TOutboxEvent> HandledBy(EventHandlerDelegate<TSpecificEvent, TProjection, TOutboxEvent> handlerDelegate)
    {
        if (string.IsNullOrEmpty(streamName))
            throw new InvalidOperationException($"Stream name must be set for event type {typeof(TSpecificEvent).Name}");

        var config = CreateStreamConfiguration();
        parent.RegisterEventHandler(config, handlerDelegate);
        return parent;
    }

    /// <summary>
    /// Completes the configuration by creating an event handler from the service provider.
    /// The handler type must implement IEventHandler&lt;TEvent, TProjection&gt; and be registered in DI.
    /// </summary>
    public EventStreamBuilder<TProjection> HandledBy<THandler>()
        where THandler : class, IEventHandler<TEvent, TProjection>
    {
        if (string.IsNullOrEmpty(streamName))
            throw new InvalidOperationException($"Stream name must be set for event type {typeof(TEvent).Name}");

        var config = CreateStreamConfiguration();
        parent.RegisterEventHandler<TEvent, THandler>(config);
        return parent;
    }

    /// <summary>
    /// Completes the configuration by creating a read-only event handler from the service provider.
    /// The handler type must implement IEventHandlerReadOnly&lt;TEvent, TProjection&gt; and be registered in DI.
    /// </summary>
    public EventStreamBuilder<TProjection> HandledByReadOnly<THandler>()
        where THandler : class, IEventHandlerReadOnly<TEvent, TProjection>
    {
        if (string.IsNullOrEmpty(streamName))
            throw new InvalidOperationException($"Stream name must be set for event type {typeof(TEvent).Name}");

        var config = CreateStreamConfiguration();
        parent.RegisterEventHandlerReadOnly<TEvent, THandler>(config);
        return parent;
    }

    private EventStreamConfiguration CreateStreamConfiguration()
    {
        return new EventStreamConfiguration
        {
            StreamName = streamName!,
            EnableDeduplication = enableDeduplication,
            RetentionPolicy = retentionPolicy,
            ShouldStoreEvent = evt => shouldStoreEvent((TEvent)evt),
            GetDeduplicationId = evt => getDeduplicationId((TEvent)evt)
        };
    }
}
