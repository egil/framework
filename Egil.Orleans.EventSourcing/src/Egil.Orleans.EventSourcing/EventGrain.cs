using Egil.Orleans.EventSourcing.EventStores;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

internal interface IEventGrain
{
    ValueTask<IEnumerable<TEvent>> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default) where TEvent : notnull;
}

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain, IEventGrain
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore eventStore;
    private readonly GrainId grainId;
    private IEventStream<TProjection>[] streams = Array.Empty<IEventStream<TProjection>>();
    private ProjectionEntry<TProjection> projectionEntry = ProjectionEntry<TProjection>.CreateDefault();
    private EventContext? activeContext;

    protected IEventStore EventStorage => eventStore;

    protected TProjection Projection
    {
        get => projectionEntry.Projection;
        private set => projectionEntry.Projection = value ?? TProjection.CreateDefault();
    }

    protected EventGrain(IEventStore eventStore)
    {
        this.eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var builder = new EventStreamBuilder<TEventGrain, TProjection>((TEventGrain)this, ServiceProvider, eventStore);
        Configure(builder);
        streams = builder.Build();
        projectionEntry = await eventStore.LoadProjectionAsync<TProjection>(grainId, cancellationToken);
    }

    protected abstract void Configure(IEventStoreConfigurator<TEventGrain, TProjection> builder);

    protected async ValueTask ProcessEventAsync<TEvent>(TEvent @event) where TEvent : notnull
    {
        var topScope = activeContext is null;
        activeContext ??= new EventContext(this);
        await ProcessEventInternalAsync<TEvent>(@event, activeContext);
    }

    private async Task ProcessEventInternalAsync<TEvent>(TEvent @event, EventContext eventContext) where TEvent : notnull
    {
        var stream = FindStream(@event);
        stream.AppendEvent(@event, projectionEntry.NextEventSequenceNumber++);
        projectionEntry.Projection = await stream.ApplyEventsAsync(projectionEntry.Projection, eventContext);

        while (streams.Any(x => x.HasUnreactedEvents))
        {
            foreach (var s in streams)
            {
                projectionEntry.Projection = await s.ApplyEventsAsync(projectionEntry.Projection, eventContext);
            }
        }
    }

    protected async ValueTask ProcessEventsAsync(Func<Task> processScope)
    {
        var context = new EventContext(this);
        try
        {
            await processScope.Invoke();
            // do stuff with activeContext
        }
        finally
        {
            activeContext = null;
        }
    }

    protected async ValueTask<IEnumerable<TEvent>> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var stream = FindStream<TEvent>(default);
        var events = await stream.GetEventsAsync(cancellationToken);
        return events.Select(x => x.TryCastEvent<TEvent>()).OfType<TEvent>();
    }

    ValueTask<IEnumerable<TEvent>> IEventGrain.GetEventsAsync<TEvent>(CancellationToken cancellationToken) => GetEventsAsync<TEvent>(cancellationToken);

    private IEventStream<TProjection> FindStream<TEvent>(TEvent? @event) where TEvent : notnull
    {
        IEventStream<TProjection>? result = null;

        foreach (var stream in streams)
        {
            if (stream.Matches(@event))
            {
                if (result is null)
                {
                    result = stream;
                }
                else
                {
                    throw new InvalidOperationException($"Multiple streams found for event type {typeof(TEvent).Name}. Ensure only one stream handles this event type. Matching streams: {result.Name}, {stream.Name}.");
                }
            }
        }

        if (result is null)
        {
            throw new InvalidOperationException($"No stream found for event type {typeof(TEvent).Name}. Ensure a stream is configured to handle this event type.");
        }

        return result;
    }

    /// <summary>
    /// Default implementation of <see cref="IEventHandlerContext"/> used during event processing.
    /// It collects events appended by handlers and provides access to the grain id,
    /// grain factory and event stream.
    /// </summary>
    private sealed class EventContext(EventGrain<TEventGrain, TProjection> eventGrain) : IEventHandlerContext, IEventReactContext
    {
        /// <inheritdoc />
        public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
        {
            eventGrain.FindStream<TEvent>(@event).AppendEvent(@event, eventGrain.projectionEntry.NextEventSequenceNumber++);
        }

        /// <inheritdoc />
        public ValueTask<IEnumerable<TEvent>> GetEventsAsync<TEvent>() where TEvent : notnull
        {
            return eventGrain.GetEventsAsync<TEvent>();
        }

        /// <inheritdoc />
        public GrainId GrainId => eventGrain.grainId;

        /// <inheritdoc />
        public IGrainFactory GrainFactory => eventGrain.GrainFactory;
    }
}
