using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using System.Collections.Concurrent;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Abstract base class for event-sourced grains with multi-stream support and outbox pattern.
/// </summary>
/// <typeparam name="TState">The type of the grain's projection state</typeparam>
public abstract class EventGrain<TState> : Grain, IEventGrain, IAsyncObserver<object>
    where TState : class, new()
{
    private readonly IEventStorage eventStorage;
    private readonly IOutboxStorage outboxStorage;
    private readonly IEventPublisher eventPublisher;
    private readonly IPersistentState<ProjectionState<TState>> projectionState;
    private readonly ILogger logger;
    private readonly ConcurrentQueue<PendingEvent> pendingEvents = new();
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly Timer? outboxTimer;
    
    private TState? currentState;
    private long lastAppliedSequenceNumber;
    private bool isReplaying;
    private volatile bool isProcessingEvents;

    /// <summary>
    /// Gets the current projection state of the grain.
    /// </summary>
    protected TState State => currentState ?? throw new InvalidOperationException("State not initialized. Ensure OnActivateAsync has completed.");

    /// <summary>
    /// Gets whether the grain is currently replaying events during recovery.
    /// </summary>
    protected bool IsReplaying => isReplaying;

    /// <summary>
    /// Gets the sequence number of the last applied event.
    /// </summary>
    protected long LastAppliedSequenceNumber => lastAppliedSequenceNumber;

    protected EventGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<TState>> projectionState,
        IEventStorage eventStorage,
        IOutboxStorage outboxStorage,
        IEventPublisher eventPublisher,
        ILogger<EventGrain<TState>> logger)
    {
        this.projectionState = projectionState;
        this.eventStorage = eventStorage;
        this.outboxStorage = outboxStorage;
        this.eventPublisher = eventPublisher;
        this.logger = logger;
        
        // Timer to periodically process outbox events
        this.outboxTimer = RegisterTimer(ProcessOutboxAsync, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    #region Public Interface

    /// <summary>
    /// Processes an event via RPC call.
    /// </summary>
    public async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        await ReceiveEventAsync(@event, cancellationToken);
    }

    /// <summary>
    /// Handles events received from Orleans streams.
    /// </summary>
    public async Task OnNextAsync(object @event, StreamSequenceToken? token = null)
    {
        await ReceiveEventAsync(@event);
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        logger.LogError(ex, "Stream error in EventGrain {GrainId}", this.GetPrimaryKeyString());
        return Task.CompletedTask;
    }

    #endregion

    #region Protected Methods for Derived Classes

    /// <summary>
    /// Configures the event streams for this grain. Must be implemented by derived classes.
    /// </summary>
    protected abstract IReadOnlyDictionary<Type, EventStreamConfiguration> ConfigureEventStreams();

    /// <summary>
    /// Applies an event to the current state. Must be implemented by derived classes.
    /// </summary>
    /// <param name="state">The current state to modify</param>
    /// <param name="event">The event to apply</param>
    protected abstract void ApplyEvent(TState state, object @event);

    /// <summary>
    /// Creates derived events based on the applied event. Override to implement domain logic.
    /// </summary>
    /// <param name="appliedEvent">The event that was just applied</param>
    /// <param name="newState">The state after applying the event</param>
    /// <returns>Events to be published via the outbox</returns>
    protected virtual IEnumerable<object> CreateDerivedEvents(object appliedEvent, TState newState)
    {
        return Enumerable.Empty<object>();
    }

    /// <summary>
    /// Determines the target stream for publishing a derived event. Override to customize routing.
    /// </summary>
    /// <param name="derivedEvent">The event to be published</param>
    /// <returns>The target stream name, or null if the event should not be published</returns>
    protected virtual string? GetTargetStreamForEvent(object derivedEvent)
    {
        return derivedEvent.GetType().Name;
    }

    /// <summary>
    /// Creates a fresh instance of the state. Override to customize state creation.
    /// </summary>
    protected virtual TState CreateInitialState() => new TState();

    /// <summary>
    /// Called when the projection state is being rebuilt from events.
    /// Override to perform custom initialization logic.
    /// </summary>
    protected virtual void OnStateRebuilding() { }

    /// <summary>
    /// Called when the projection state has been successfully rebuilt from events.
    /// Override to perform custom completion logic.
    /// </summary>
    protected virtual void OnStateRebuilt() { }

    #endregion

    #region Grain Lifecycle

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        var grainId = this.GetPrimaryKeyString();
        logger.LogDebug("Activating EventGrain {GrainId}", grainId);

        // Load projection state
        await projectionState.ReadStateAsync();
        
        if (projectionState.State.State != null)
        {
            // We have a persisted projection, check if we need to catch up
            currentState = projectionState.State.State;
            lastAppliedSequenceNumber = projectionState.State.LastAppliedSequenceNumber;
            
            // Check for any events that came after our last applied sequence
            var recentEvents = await eventStorage.GetEventsFromSequenceAsync(grainId, lastAppliedSequenceNumber + 1, cancellationToken);
            
            if (recentEvents.Count > 0)
            {
                logger.LogInformation("Catching up {EventCount} events for grain {GrainId}", recentEvents.Count, grainId);
                await ReplayEvents(recentEvents, isFullReplay: false, cancellationToken);
            }
        }
        else
        {
            // No persisted state, rebuild from event log
            logger.LogInformation("No projection state found for grain {GrainId}, rebuilding from events", grainId);
            await RebuildStateFromEvents(cancellationToken);
        }

        // Process any pending outbox events
        await ProcessOutboxAsync(null);
        
        logger.LogDebug("EventGrain {GrainId} activated successfully", grainId);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Ensure any pending events are processed before deactivation
        if (isProcessingEvents)
        {
            await processingLock.WaitAsync(cancellationToken);
            processingLock.Release();
        }

        outboxTimer?.Dispose();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    #endregion

    #region Private Implementation

    private async Task ReceiveEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        var grainId = this.GetPrimaryKeyString();
        var eventType = @event.GetType();
        var streamConfigurations = ConfigureEventStreams();

        if (!streamConfigurations.TryGetValue(eventType, out var config))
        {
            logger.LogWarning("No stream configuration found for event type {EventType} in grain {GrainId}", eventType.Name, grainId);
            return;
        }

        if (!config.ShouldStoreEvent(@event))
        {
            logger.LogDebug("Event {EventType} filtered out by stream configuration for grain {GrainId}", eventType.Name, grainId);
            return;
        }

        // Extract deduplication ID if needed
        string? deduplicationId = null;
        if (config.EnableDeduplicationById && config.GetEventId != null)
        {
            deduplicationId = config.GetEventId(@event);
        }

        try
        {
            // Handle deduplication
            if (!string.IsNullOrEmpty(deduplicationId))
            {
                var existingEvent = await eventStorage.FindEventByDeduplicationIdAsync(grainId, config.StreamName, deduplicationId, cancellationToken);
                if (existingEvent != null)
                {
                    // Remove the old event before storing the new one
                    await eventStorage.RemoveEventsAsync(grainId, [(config.StreamName, existingEvent.SequenceNumber)], cancellationToken);
                }
            }

            // Store the event
            var storedEvent = await eventStorage.AppendEventAsync(grainId, config.StreamName, @event, deduplicationId, cancellationToken);
            
            // Apply retention policy
            await ApplyRetentionPolicy(grainId, config, cancellationToken);

            // Queue for asynchronous processing
            pendingEvents.Enqueue(new PendingEvent(storedEvent));
            
            // Trigger background processing
            _ = Task.Run(() => ProcessPendingEventsAsync(CancellationToken.None));
            
            logger.LogDebug("Event {EventType} stored with sequence {SequenceNumber} for grain {GrainId}", 
                eventType.Name, storedEvent.SequenceNumber, grainId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store event {EventType} for grain {GrainId}", eventType.Name, grainId);
            throw;
        }
    }

    private async Task ProcessPendingEventsAsync(CancellationToken cancellationToken)
    {
        if (isProcessingEvents || !await processingLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            isProcessingEvents = true;
            
            while (pendingEvents.TryDequeue(out var pendingEvent))
            {
                await ProcessSingleEventAsync(pendingEvent.StoredEvent, cancellationToken);
            }
        }
        finally
        {
            isProcessingEvents = false;
            processingLock.Release();
        }
    }

    private async Task ProcessSingleEventAsync(StoredEvent storedEvent, CancellationToken cancellationToken)
    {
        try
        {
            // Apply event to state
            ApplyEvent(currentState!, storedEvent.Event);
            lastAppliedSequenceNumber = storedEvent.SequenceNumber;

            // Create derived events (only if not replaying)
            var derivedEvents = isReplaying ? Enumerable.Empty<object>() : CreateDerivedEvents(storedEvent.Event, currentState!);
            var outboxEvents = derivedEvents.Select(CreateOutboxEvent).ToList();

            // Persist projection and outbox events atomically
            projectionState.State = new ProjectionState<TState>
            {
                State = currentState!,
                LastAppliedSequenceNumber = lastAppliedSequenceNumber
            };

            if (outboxEvents.Count > 0)
            {
                await outboxStorage.AddOutboxEventsAsync(this.GetPrimaryKeyString(), outboxEvents, cancellationToken);
            }

            await projectionState.WriteStateAsync();

            // Mark event as handled
            await eventStorage.MarkEventsAsHandledAsync(this.GetPrimaryKeyString(), 
                [(storedEvent.StreamName, storedEvent.SequenceNumber)], cancellationToken);

            logger.LogDebug("Processed event {SequenceNumber} for grain {GrainId}, created {OutboxCount} outbox events", 
                storedEvent.SequenceNumber, this.GetPrimaryKeyString(), outboxEvents.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process event {SequenceNumber} for grain {GrainId}", 
                storedEvent.SequenceNumber, this.GetPrimaryKeyString());
            
            // Re-queue for retry
            pendingEvents.Enqueue(new PendingEvent(storedEvent));
            throw;
        }
    }

    private async Task RebuildStateFromEvents(CancellationToken cancellationToken)
    {
        var grainId = this.GetPrimaryKeyString();
        
        OnStateRebuilding();
        isReplaying = true;
        
        try
        {
            currentState = CreateInitialState();
            lastAppliedSequenceNumber = 0;

            var allEvents = await eventStorage.GetEventsAsync(grainId, cancellationToken);
            await ReplayEvents(allEvents, isFullReplay: true, cancellationToken);
            
            OnStateRebuilt();
        }
        finally
        {
            isReplaying = false;
        }
    }

    private async Task ReplayEvents(IReadOnlyList<StoredEvent> events, bool isFullReplay, CancellationToken cancellationToken)
    {
        var wasReplaying = isReplaying;
        if (isFullReplay)
            isReplaying = true;

        try
        {
            foreach (var storedEvent in events.OrderBy(e => e.SequenceNumber))
            {
                if (storedEvent.IsHandled || isFullReplay)
                {
                    // Only apply to projection, don't create derived events
                    ApplyEvent(currentState!, storedEvent.Event);
                    lastAppliedSequenceNumber = storedEvent.SequenceNumber;
                }
                else
                {
                    // This is a catch-up event, process normally
                    await ProcessSingleEventAsync(storedEvent, cancellationToken);
                }
            }

            // Save the rebuilt/updated state
            projectionState.State = new ProjectionState<TState>
            {
                State = currentState!,
                LastAppliedSequenceNumber = lastAppliedSequenceNumber
            };
            await projectionState.WriteStateAsync();
        }
        finally
        {
            if (isFullReplay)
                isReplaying = wasReplaying;
        }
    }

    private async Task ApplyRetentionPolicy(string grainId, EventStreamConfiguration config, CancellationToken cancellationToken)
    {
        var streamEvents = await eventStorage.GetEventsAsync(grainId, config.StreamName, cancellationToken);
        var eventsToRemove = streamEvents.Where(e => !config.RetentionPolicy.ShouldRetain(e, streamEvents)).ToList();
        
        if (eventsToRemove.Count > 0)
        {
            var eventsToRemoveIds = eventsToRemove.Select(e => (e.StreamName, e.SequenceNumber));
            await eventStorage.RemoveEventsAsync(grainId, eventsToRemoveIds, cancellationToken);
            
            logger.LogDebug("Removed {Count} events from stream {StreamName} for grain {GrainId} due to retention policy", 
                eventsToRemove.Count, config.StreamName, grainId);
        }
    }

    private OutboxEvent CreateOutboxEvent(object derivedEvent)
    {
        return new OutboxEvent
        {
            Id = Guid.NewGuid().ToString(),
            GrainId = this.GetPrimaryKeyString(),
            Event = derivedEvent,
            CreatedAt = DateTime.UtcNow,
            EventTypeName = derivedEvent.GetType().FullName!,
            TargetStream = GetTargetStreamForEvent(derivedEvent)
        };
    }

    private async Task ProcessOutboxAsync(object? _)
    {
        try
        {
            var grainId = this.GetPrimaryKeyString();
            var pendingOutboxEvents = await outboxStorage.GetPendingOutboxEventsAsync(grainId);
            
            if (pendingOutboxEvents.Count == 0)
                return;

            var successfullyPublished = new List<string>();
            
            foreach (var outboxEvent in pendingOutboxEvents)
            {
                try
                {
                    await eventPublisher.PublishAsync(outboxEvent);
                    successfullyPublished.Add(outboxEvent.Id);
                    
                    logger.LogDebug("Published outbox event {EventId} of type {EventType} for grain {GrainId}", 
                        outboxEvent.Id, outboxEvent.EventTypeName, grainId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to publish outbox event {EventId} for grain {GrainId}, will retry", 
                        outboxEvent.Id, grainId);
                }
            }

            if (successfullyPublished.Count > 0)
            {
                await outboxStorage.RemoveOutboxEventsAsync(grainId, successfullyPublished);
                logger.LogDebug("Removed {Count} successfully published outbox events for grain {GrainId}", 
                    successfullyPublished.Count, grainId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing outbox for grain {GrainId}", this.GetPrimaryKeyString());
        }
    }

    #endregion

    private record PendingEvent(StoredEvent StoredEvent);
}

/// <summary>
/// Represents the persisted projection state.
/// </summary>
/// <typeparam name="TState">The type of the projection state</typeparam>
public class ProjectionState<TState> where TState : class
{
    public TState? State { get; set; }
    public long LastAppliedSequenceNumber { get; set; }
}
    {
        try
        {
            var storedStateValid = AssignProjectionPropertiesFromPersistentStateOrDefault();
            var appliedEvents = await ApplyMissingEvents(cancellationToken);
            if (appliedEvents > 0 && writeStateIfStoredStateInvalid && !storedStateValid)
            {
                await WriteStateAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailedToReadStateFromStorage(ex, this.GetGrainId());
        }
    }

    /// <summary>
    /// Assigns the <see cref="State"/> and <see cref="Version"/> properties from
    /// <see cref="projectionStorage"/> if the persisted state is compatible with <typeparamref name="TState"/>.
    /// If it is not compatible based on <see cref="Projection{TState}.MetadataHashCode"/>, then <see cref="State"/>
    /// is set to the returned value from <see cref="CreateDefaultState"/> and <see cref="Version"/> is set to <c>0</c>.
    /// </summary>
    /// <returns><see langword="true"/> if state was set from <see cref="projectionStorage"/>, <see langword="false"/> otherwise.</returns>
    [MemberNotNull(nameof(State))]
    private bool AssignProjectionPropertiesFromPersistentStateOrDefault()
    {
        if (projectionStorage.RecordExists && projectionStorage.State.State is not null && projectionStorage.State.MetadataHashCode == StateMetadataHashCode)
        {
            State = projectionStorage.State.State;
            Version = projectionStorage.State.Version;
            return true;
        }
        else
        {
            State = CreateDefaultState()!;
            Version = 0;
            return false;
        }
    }

    /// <summary>
    /// Writes the current <see cref="State"/> projection to storage, effectively creating a snapshot.
    /// </summary>
    protected async ValueTask WriteStateAsync(CancellationToken cancellationToken = default)
    {
        if (projectionStorage.State.Version == Version)
        {
            return;
        }

        var originalState = projectionStorage.State;

        try
        {
            projectionStorage.State = new Projection<TState>
            {
                State = State,
                Version = Version,
                MetadataHashCode = StateMetadataHashCode
            };

            await projectionStorage.WriteStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailedToWriteStateToStorage(ex, this.GetGrainId());
            projectionStorage.State = originalState;
            throw;
        }
    }

    /// <summary>
    /// Clears the persisted state projection from storage.
    /// </summary>
    protected async ValueTask ClearStateAsync(CancellationToken cancellationToken = default)
    {
        await projectionStorage.ClearStateAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the events from the <typeparamref name="TEvent"/> log starting from the specified <paramref name="fromVersion"/>.
    /// </summary>
    protected IAsyncEnumerable<TEvent> GetEventsAsync(int fromVersion = 0, CancellationToken cancellationToken = default)
        => eventLogStorage.ReadEventsAsync(fromVersion, cancellationToken);

    /// <summary>
    /// Creates the default state of the <typeparamref name="TState"/>. This is the starting point of the projection.
    /// </summary>
    protected virtual TState CreateDefaultState()
        => Activator.CreateInstance<TState>();

    /// <summary>
    /// Applies the <paramref name="event"/> to the <paramref name="state"/>.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="state">The current state to apply the event to.</param>
    /// <returns>The state after the event has been applied.</returns>
    protected abstract TState ApplyEvent(TEvent @event, TState state);

    private void ApplyEvent(TEvent evt)
    {
        State = ApplyEvent(evt, State);
        Version++;
    }

    private async ValueTask<int> ApplyMissingEvents(CancellationToken cancellationToken)
    {
        var appliedEvents = 0;

        await foreach (var @event in GetEventsAsync(Version).WithCancellation(cancellationToken))
        {
            ApplyEvent(@event);
            appliedEvents++;
        }

        return appliedEvents;
    }

    private sealed class StorageInitializer : ILifecycleObserver, IGrainMigrationParticipant
    {
        private readonly EventSourcedGrain<TEvent, TState> grain;

        private StorageInitializer(EventSourcedGrain<TEvent, TState> grain) => this.grain = grain;

        public void OnDehydrate(IDehydrationContext dehydrationContext)
            => (grain.projectionStorage as IGrainMigrationParticipant)?.OnDehydrate(dehydrationContext);

        public void OnRehydrate(IRehydrationContext rehydrationContext)
        {
            (grain.projectionStorage as IGrainMigrationParticipant)?.OnRehydrate(rehydrationContext);
            grain.AssignProjectionPropertiesFromPersistentStateOrDefault();
        }

        public async Task OnStart(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            grain.AssignProjectionPropertiesFromPersistentStateOrDefault();
            await grain.ReadStateAsync(writeStateIfStoredStateInvalid: true, cancellationToken);
        }

        public Task OnStop(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public static StorageInitializer Create(EventSourcedGrain<TEvent, TState> grain)
        {
            var observer = new StorageInitializer(grain);
            grain.GrainContext.ObservableLifecycle.AddMigrationParticipant(observer);
            grain.GrainContext.ObservableLifecycle.Subscribe(
                RuntimeTypeNameFormatter.Format(grain.GetType()),
                GrainLifecycleStage.SetupState + 1, // ensure this runs just after SetupState where grain.projectionStorage runs.
                observer);
            return observer;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to append all events to the log. Successfully appended {AppendedEvents} of {TotalEvents} events.")]
    private partial void LogFailedAppendAllEvents(int appendedEvents, int totalEvents);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to apply all events to state after appending them to the log. Applied {AppliedEvents} of {AppendedEvents} events.")]
    private partial void LogFailedToApplyAllAppendedEvents(int appliedEvents, int appendedEvents);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read state from storage for grain {GrainId}. Creating new state.")]
    private partial void LogFailedToReadStateFromStorage(Exception exception, GrainId grainId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write state to storage for grain {GrainId}. Reverting to previous state.")]
    private partial void LogFailedToWriteStateToStorage(Exception exception, GrainId grainId);
}
