using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Abstract base class for event-sourced grains with Orleans-style attribute-based dependency injection.
/// Supports both Orleans persistent state and shared transaction scope with event storage.
/// Uses Orleans' keyed services pattern similar to IPersistentState&lt;T&gt;.
/// </summary>
/// <typeparam name="TProjection">The type of the immutable projection state</typeparam>
/// <typeparam name="TEvent">The domain event type</typeparam>
/// <typeparam name="TOutboxEvent">The outbox event type</typeparam>
public abstract class EventGrain<TProjection, TEvent, TOutboxEvent> : Grain, IEventGrain
    where TProjection : notnull, IEventProjection<TProjection>
    where TEvent : class
    where TOutboxEvent : class
{
    private readonly IEventStorage<TEvent, TOutboxEvent> eventStorage;
    private readonly IPersistentState<ProjectionState<TProjection>>? projectionState;
    private readonly IEventPublisher? eventPublisher;
    private readonly OutboxPostmanService? outboxPostmanService;
    protected readonly ILogger logger;

    private TProjection? currentProjection;
    private long lastAppliedSequenceNumber;
    private bool isReplaying;
    private readonly bool useSharedTransactionScope;

    /// <summary>
    /// Gets the current projection state of the grain.
    /// </summary>
    protected TProjection Projection => currentProjection ?? TProjection.CreateDefault();

    /// <summary>
    /// Gets whether the grain is currently replaying events during recovery.
    /// </summary>
    protected bool IsReplaying => isReplaying;

    /// <summary>
    /// Gets the sequence number of the last applied event.
    /// </summary>
    protected long LastAppliedSequenceNumber => lastAppliedSequenceNumber;

    /// <summary>
    /// Constructor for EventGrain with Orleans persistent state and Orleans-style event storage injection.
    /// This is the recommended constructor for new implementations.
    /// </summary>
    /// <example>
    /// <code>
    /// public class UserGrain : EventGrain&lt;UserProjection, UserEvent, UserOutboxEvent&gt;
    /// {
    ///     public UserGrain(
    ///         [PersistentState("projection")] IPersistentState&lt;ProjectionState&lt;UserProjection&gt;&gt; projectionState,
    ///         [FromKeyedServices("user-events")] IEventStorage&lt;UserEvent, UserOutboxEvent&gt; eventStorage,
    ///         ILogger&lt;UserGrain&gt; logger) 
    ///         : base(projectionState, eventStorage, logger)
    ///     {
    ///     }
    /// }
    /// </code>
    /// </example>
    protected EventGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<TProjection>> projectionState,
        [FromKeyedServices("default")] IEventStorage<TEvent, TOutboxEvent> eventStorage,
        IEventPublisher? eventPublisher = null,
        OutboxPostmanService? outboxPostmanService = null,
        ILogger? logger = null)
    {
        this.projectionState = projectionState ?? throw new ArgumentNullException(nameof(projectionState));
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        this.eventPublisher = eventPublisher;
        this.outboxPostmanService = outboxPostmanService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        this.useSharedTransactionScope = false;
    }

    /// <summary>
    /// Constructor for EventGrain with shared transaction scope (projection stored with events).
    /// When IPersistentState is not provided, the projection will be stored in the same 
    /// transaction scope as the events, typically in table storage.
    /// </summary>
    /// <example>
    /// <code>
    /// public class UserGrain : EventGrain&lt;UserProjection, UserEvent, UserOutboxEvent&gt;
    /// {
    ///     public UserGrain(
    ///         [FromKeyedServices("user-events")] IEventStorage&lt;UserEvent, UserOutboxEvent&gt; eventStorage,
    ///         ILogger&lt;UserGrain&gt; logger) 
    ///         : base(eventStorage, logger)
    ///     {
    ///     }
    /// }
    /// </code>
    /// </example>
    protected EventGrain(
        [FromKeyedServices("default")] IEventStorage<TEvent, TOutboxEvent> eventStorage,
        IEventPublisher? eventPublisher = null,
        OutboxPostmanService? outboxPostmanService = null,
        ILogger? logger = null)
    {
        this.projectionState = null;
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        this.eventPublisher = eventPublisher;
        this.outboxPostmanService = outboxPostmanService;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        this.useSharedTransactionScope = true;
    }

    #region Public Interface

    /// <summary>
    /// Processes an event via RPC call.
    /// </summary>
    public abstract Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default);

    #endregion

    #region Protected Methods for Derived Classes

    /// <summary>
    /// Configures the event streams and handlers for this grain using a fluent, strongly-typed builder.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract void ConfigureEventStreams(EventStreamBuilder<TProjection, TEvent, TOutboxEvent> builder);

    /// <summary>
    /// Gets the service provider for resolving event handlers and other dependencies.
    /// </summary>
    protected IServiceProvider Services => this.ServiceProvider;

    /// <summary>
    /// Creates a fresh instance of the projection. 
    /// Uses the static interface method from IEventProjection&lt;T&gt;.
    /// </summary>
    protected virtual TProjection CreateInitialProjection() => TProjection.CreateDefault();

    /// <summary>
    /// Gets the current projection schema version. Override to handle projection migrations.
    /// </summary>
    protected virtual int ProjectionVersion => 1;

    /// <summary>
    /// Called when the projection state is being rebuilt from events.
    /// Override to perform custom initialization logic.
    /// </summary>
    protected virtual void OnProjectionRebuilding() { }

    /// <summary>
    /// Called when the projection state has been successfully rebuilt from events.
    /// Override to perform custom completion logic.
    /// </summary>
    protected virtual void OnProjectionRebuilt() { }

    /// <summary>
    /// Migrates an old projection to the current version.
    /// Override to handle breaking changes in projection structure.
    /// </summary>
    protected virtual TProjection MigrateProjection(TProjection oldProjection, int fromVersion, int toVersion)
    {
        return oldProjection; // Default: no migration needed
    }

    /// <summary>
    /// Processes an event using the configured stream handlers.
    /// </summary>
    protected async Task<EventProcessingResult<TProjection, TOutboxEvent>> ProcessEventWithHandlerAsync(
        object @event, 
        CancellationToken cancellationToken = default)
    {
        var eventType = @event.GetType();
        var (streamConfigs, handlers) = GetConfiguredHandlers();

        if (!streamConfigs.TryGetValue(eventType, out var streamConfig))
        {
            throw new InvalidOperationException($"No stream configuration found for event type {eventType.Name}");
        }

        if (!handlers.TryGetValue(eventType, out var handler))
        {
            throw new InvalidOperationException($"No event handler found for event type {eventType.Name}");
        }

        // Check if this handler supports outbox (determines if we pass an outbox instance)
        var hasOutboxSupport = RequiresOutbox(eventType);
        var outbox = hasOutboxSupport ? new EventOutbox<TOutboxEvent>() : null;
        
        // Apply the event to get the updated projection
        var updatedProjection = await handler(@event, Projection, outbox);
        
        // Generate sequence number and create result
        var sequenceNumber = lastAppliedSequenceNumber + 1;
        var result = new EventProcessingResult<TProjection, TOutboxEvent>(
            updatedProjection, 
            sequenceNumber,
            outbox?.ToImmutable() ?? Outbox<TOutboxEvent>.Empty);

        // Store the event and update projection
        await StoreEventAndUpdateProjectionAsync(@event, result, cancellationToken);
        
        return result;
    }

    /// <summary>
    /// Checks if the handler for the given event type requires outbox support.
    /// This is determined during handler registration.
    /// </summary>
    private bool RequiresOutbox(Type eventType)
    {
        // For now, we'll determine this based on the handler registration
        // This could be tracked during registration to optimize performance
        return true; // Default to supporting outbox for now
    }

    /// <summary>
    /// Rebuilds the projection from the event log.
    /// </summary>
    protected async Task<TProjection> RebuildProjectionFromEventsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Rebuilding projection from events for grain {GrainId}", this.GetPrimaryKeyString());
        
        OnProjectionRebuilding();
        isReplaying = true;
        
        try
        {
            var projection = CreateInitialProjection();
            var lastSequence = 0L;
            
            // Get configured handlers for event replay
            var (streamConfigs, handlers) = GetConfiguredHandlers();
            
            await foreach (var storedEvent in eventStorage.ReadEventsAsync(0, cancellationToken))
            {
                var eventType = storedEvent.Event.GetType();
                
                if (handlers.TryGetValue(eventType, out var handler))
                {
                    // During replay, we don't use outbox
                    projection = await handler(storedEvent.Event, projection, null);
                    lastSequence = storedEvent.SequenceNumber;
                }
            }
            
            lastAppliedSequenceNumber = lastSequence;
            
            OnProjectionRebuilt();
            return projection;
        }
        finally
        {
            isReplaying = false;
        }
    }

    /// <summary>
    /// Stores the event and updates the projection state atomically.
    /// </summary>
    private async Task StoreEventAndUpdateProjectionAsync(
        object @event, 
        EventProcessingResult<TProjection, TOutboxEvent> result, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Store the event first
            TEvent typedEvent = (TEvent)@event;
            var appendResult = await eventStorage.AppendEventsAsync([typedEvent], cancellationToken);
            
            // Update sequence number based on actual storage result
            lastAppliedSequenceNumber = appendResult.LastSequenceNumber;
            currentProjection = result.Projection;
            
            // Store projection state
            await UpdateProjectionStateAsync(cancellationToken);
            
            // Store outbox events if any
            if (result.Outbox.Events.Count > 0)
            {
                await StoreOutboxEventsAsync(result.Outbox, cancellationToken);
            }
            
            logger.LogDebug("Successfully stored event and updated projection for grain {GrainId}", 
                this.GetPrimaryKeyString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store event and update projection for grain {GrainId}", 
                this.GetPrimaryKeyString());
            throw;
        }
    }

    /// <summary>
    /// Updates the projection state based on the storage configuration.
    /// </summary>
    private async Task UpdateProjectionStateAsync(CancellationToken cancellationToken = default)
    {
        var projectionState = new ProjectionState<TProjection>(
            currentProjection,
            lastAppliedSequenceNumber,
            ProjectionVersion);

        if (useSharedTransactionScope)
        {
            // Store projection with events in shared transaction scope
            await eventStorage.WriteProjectionAsync(projectionState, cancellationToken);
        }
        else
        {
            // Store projection in Orleans persistent state
            this.projectionState!.State = projectionState;
            await this.projectionState.WriteStateAsync();
        }
    }

    /// <summary>
    /// Stores outbox events for later processing.
    /// </summary>
    private async Task StoreOutboxEventsAsync(Outbox<TOutboxEvent> outbox, CancellationToken cancellationToken = default)
    {
        if (outbox.Events.Count == 0 && outbox.StreamTargets.Count == 0 && outbox.CustomActions.Count == 0) 
            return;

        var grainId = this.GetPrimaryKeyString();
        
        // Store regular outbox events
        if (outbox.Events.Count > 0)
        {
            await eventStorage.AddOutboxEventsAsync(grainId, outbox.Events, cancellationToken);
        }

        // TODO: Process Orleans stream targets
        // This would integrate with Orleans Streams to send events to specified streams
        
        // TODO: Process custom actions
        // These would be executed immediately or queued for later processing
    }

    /// <summary>
    /// Catches up the projection with events that occurred after the last applied sequence.
    /// </summary>
    private async Task CatchUpWithEventStreamAsync(CancellationToken cancellationToken = default)
    {
        var (streamConfigs, handlers) = GetConfiguredHandlers();
        var eventsApplied = 0;

        await foreach (var storedEvent in eventStorage.ReadEventsAsync(lastAppliedSequenceNumber + 1, cancellationToken))
        {
            var eventType = storedEvent.Event.GetType();
            
            if (handlers.TryGetValue(eventType, out var handler))
            {
                // During catch-up, we don't use outbox (similar to replay)
                currentProjection = await handler(storedEvent.Event, currentProjection!, null);
                lastAppliedSequenceNumber = storedEvent.SequenceNumber;
                eventsApplied++;
            }
        }

        if (eventsApplied > 0)
        {
            logger.LogInformation("Caught up projection with {EventsApplied} events for grain {GrainId}", 
                eventsApplied, this.GetPrimaryKeyString());
            
            // Update the stored projection state after catching up
            await UpdateProjectionStateAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Gets the configured stream configurations and event handlers.
    /// </summary>
    private (IReadOnlyDictionary<Type, EventStreamConfiguration<TEvent>> StreamConfigs, 
             IReadOnlyDictionary<Type, Func<object, TProjection, EventOutbox<TOutboxEvent>?, ValueTask<TProjection>>> Handlers) GetConfiguredHandlers()
    {
        var builder = new EventStreamBuilder<TProjection, TEvent, TOutboxEvent>(Services);
        ConfigureEventStreams(builder);
        return (builder.GetStreamConfigurations(), builder.GetEventHandlers());
    }

    #endregion

    #region Grain Lifecycle

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        if (useSharedTransactionScope)
        {
            // Projection is stored with events in shared transaction scope
            var projectionData = await eventStorage.ReadProjectionAsync<TProjection>(cancellationToken);
            
            if (projectionData?.Projection != null)
            {
                var projection = projectionData.Projection;
                var lastSequence = projectionData.LastAppliedSequenceNumber;
                var version = projectionData.Version;
                
                // Handle projection migration if needed
                if (version != ProjectionVersion)
                {
                    currentProjection = MigrateProjection(projection, version, ProjectionVersion);
                }
                else
                {
                    currentProjection = projection;
                }
                
                lastAppliedSequenceNumber = lastSequence;
            }
            else
            {
                // No projection found, rebuild from events
                currentProjection = await RebuildProjectionFromEventsAsync(cancellationToken);
            }
        }
        else
        {
            // Load from Orleans persistent state
            await projectionState!.ReadStateAsync();
            
            if (projectionState.State.Projection != null)
            {
                // Handle projection migration if needed
                if (projectionState.State.Version != ProjectionVersion)
                {
                    currentProjection = MigrateProjection(
                        projectionState.State.Projection, 
                        projectionState.State.Version, 
                        ProjectionVersion);
                }
                else
                {
                    currentProjection = projectionState.State.Projection;
                }
                
                lastAppliedSequenceNumber = projectionState.State.LastAppliedSequenceNumber;
                
                // Check for events that came after our last applied sequence and catch up
                await CatchUpWithEventStreamAsync(cancellationToken);
            }
            else
            {
                // No persisted state, rebuild from event log
                currentProjection = await RebuildProjectionFromEventsAsync(cancellationToken);
            }
        }

        // Process any pending outbox events
        await ProcessPendingOutboxEventsAsync();
    }

    #endregion

    #region Private Implementation Stubs

    private async Task ProcessPendingOutboxEventsAsync()
    {
        if (outboxPostmanService == null) return;

        var grainId = this.GetPrimaryKeyString();
        var pendingEvents = await eventStorage.GetPendingOutboxEventsAsync(grainId);
        
        var processedEventIds = new List<string>();
        
        foreach (var outboxEvent in pendingEvents)
        {
            try
            {
                var success = await outboxPostmanService.ProcessEventAsync(outboxEvent.Event);
                if (success)
                {
                    processedEventIds.Add(outboxEvent.Id);
                }
                else
                {
                    // Update retry count
                    await eventStorage.UpdateOutboxEventRetryAsync(
                        grainId, 
                        outboxEvent.Id, 
                        outboxEvent.RetryCount + 1, 
                        DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process outbox event {EventId}", outboxEvent.Id);
                
                // Update retry count
                await eventStorage.UpdateOutboxEventRetryAsync(
                    grainId, 
                    outboxEvent.Id, 
                    outboxEvent.RetryCount + 1, 
                    DateTime.UtcNow);
            }
        }
        
        // Remove successfully processed events
        if (processedEventIds.Count > 0)
        {
            await eventStorage.RemoveOutboxEventsAsync(grainId, processedEventIds);
        }
    }

    #endregion
}
