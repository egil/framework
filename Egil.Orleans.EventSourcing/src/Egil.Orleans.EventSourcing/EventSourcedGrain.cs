using Egil.Orleans.EventSourcing.AzureStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using System.Reflection;

namespace Egil.Orleans.EventSourcing;

public abstract partial class EventSourcedGrain<TEvent, TState> : Grain
{
    private readonly IEventStorage<TEvent> eventLogStorage;
    private readonly IPersistentState<Projection<TState>> projectionStorage;
    private readonly ILogger<EventSourcedGrain<TEvent, TState>> logger;
    private TState? state;

    /// <summary>
    /// Gets the hash code used to validate if the persisted projection
    /// state is compatible with the current <typeparamref name="TState"/>.
    /// If the persisted state is not compatible, the state will be created and
    /// saved from the <typeparamref name="TEvent"/> log.
    /// </summary>
    /// <remarks>
    /// The default hash code is generated from the public properties of the <typeparamref name="TState"/> type.
    /// </remarks>
    protected virtual int StateMetadataHashCode { get; } = GetTStateMetadataHashCode();

    /// <summary>
    /// Gets the projected state of the <typeparamref name="TEvent"/> log.
    /// </summary>
    protected TState State { get => state ?? CreateDefaultState(); private set => state = value; }

    /// <summary>
    /// Gets whether the <see cref="State"/> is newer than what is persisted to storage.
    /// </summary>
    protected bool IsStateDirty => projectionStorage.State.Version != Version;

    /// <summary>
    /// Gets the version of the <typeparamref name="TEvent"/> log that has been applied to the <see cref="State"/>.
    /// </summary>
    protected int Version { get; private set; }

    protected EventSourcedGrain(IPersistentState<Projection<TState>> projectionStorage)
    {
        if (ServiceProvider is not { } serviceProvider)
        {
            throw new InvalidOperationException("Cannot instantiate EventSourcedGrain without a runtime.");
        }

        //projectionStorage = serviceProvider.GetRequiredService<IPersistentStateFactory>()
        //    .Create<Projection<TState>>(GrainContext, projectionStateConfiguration);
        this.projectionStorage = projectionStorage;

        eventLogStorage = serviceProvider
            .GetRequiredService<IAzureAppendBlobEventStorageProvider>()
            .Create<TEvent>(GrainContext);

        Version = 0;
        logger = serviceProvider.GetService<ILogger<EventSourcedGrain<TEvent, TState>>>()
            ?? NullLogger<EventSourcedGrain<TEvent, TState>>.Instance;
    }

    /// <summary>
    /// Reads the projected state from storage and applies any missing events.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await ReadStateAsync(writeStateIfStoredStateInvalid: true, cancellationToken);
    }

    /// <summary>
    /// Saves the <paramref name="event"/> to the <typeparamref name="TEvent"/> log and applies it to the <see cref="State"/>.
    /// </summary>
    /// <remarks>
    /// This does NOT save the projected <see cref="State"/> to storage. Explicitly
    /// call <see cref="WriteStateAsync"/> to save the state.
    /// </remarks>
    /// <returns>The number of events that were saved and applied.</returns>

    protected async ValueTask<(TState Before, TState After)> RaiseEventAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        var appendedEvent = await eventLogStorage.AppendEventAsync(@event, cancellationToken);

        if (appendedEvent == 0)
        {
            return (State, State);
        }

        var before = State;
        ApplyEvent(@event);
        return (before, State);
    }

    /// <summary>
    /// Reads the persisted <see cref="State"/> projection and updates it based on
    /// the <typeparamref name="TEvent"/> log. If the persisted state was not found or is incompatible with the
    /// current version of <typeparamref name="TState"/>, the state will be created and saved from
    /// the <typeparamref name="TEvent"/> log.
    /// </summary>
    protected ValueTask ReadStateAsync(CancellationToken cancellationToken = default)
        => ReadStateAsync(writeStateIfStoredStateInvalid: false, cancellationToken);

    private async ValueTask ReadStateAsync(bool writeStateIfStoredStateInvalid, CancellationToken cancellationToken)
    {
        try
        {
            await projectionStorage.ReadStateAsync(cancellationToken);

            if (projectionStorage.RecordExists && projectionStorage.State.State is not null && projectionStorage.State.MetadataHashCode == StateMetadataHashCode)
            {
                State = projectionStorage.State.State;
                Version = projectionStorage.State.Version;
                await ApplyMissingEvents(cancellationToken);
                return;
            }
        }
        catch (Exception ex)
        {
            LogFailedToReadStateFromStorage(ex, this.GetGrainId());
        }

        State = CreateDefaultState();
        Version = 0;

        var appliedEvents = await ApplyMissingEvents(cancellationToken);

        if (appliedEvents > 0 && writeStateIfStoredStateInvalid)
        {
            await WriteStateAsync(cancellationToken);
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

    private static int GetTStateMetadataHashCode()
    {
        var hash = new HashCode();

        foreach (var property in typeof(TState).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            hash.Add(property.Name);
            hash.Add(property.PropertyType.FullName);
        }

        return hash.ToHashCode();
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
