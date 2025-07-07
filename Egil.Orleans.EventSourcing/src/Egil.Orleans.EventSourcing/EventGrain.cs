using Orleans;
using Egil.Orleans.EventSourcing.Internal;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore eventStorage;
    private readonly GrainId grainId;
    private readonly IEventStream<TProjection>[] streams;
    private EventContext? activeContext;

    protected EventGrain(IEventStore eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
        var builder = new EventStreamBuilder<TEventGrain, TProjection>();
        Configure(builder);
        streams = builder.Build();
    }

    protected IEventStore EventStorage => eventStorage;

    protected TProjection Projection { get; set; }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected async ValueTask ProcessEventAsync<TEvent>(TEvent @event) where TEvent : notnull
    {
        var topScope = activeContext is null;
        activeContext ??= new EventContext(grainId, eventStorage, GrainFactory);

        try
        {
            var partition = FindStream<TEvent>(@event);

            foreach (var handlerFactory in partition.Handlers)
            {
                if (handlerFactory.TryCreate(@event, this, ServiceProvider) is { } handler)
                {
                    Projection = await handler.HandleAsync(@event, Projection, activeContext);
                }
            }

            if (topScope)
            {
                // do stuff with activeContext
            }
        }
        finally
        {
            if (topScope)
            {
                activeContext = null;
            }
        }
    }

    protected async ValueTask ProcessEventAsync(Func<Task> processScope)
    {
        var context = new EventContext(grainId, eventStorage, GrainFactory);
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

    private IEventStream<TProjection> FindStream<TEvent>(TEvent @event) where TEvent : notnull
    {
        IEventStream<TProjection>? result = null;

        foreach (var stream in streams)
        {
            if (stream.TryCast(@event) is { } compatibleStream)
            {
                if (result is null)
                {
                    result = compatibleStream;
                }
                else
                {
                    throw new InvalidOperationException($"Multiple streams found for event type {typeof(TEvent).Name}. Ensure only one stream handles this event type.");
                }
            }
        }

        if (result is null)
        {
            throw new InvalidOperationException($"No stream found for event type {typeof(TEvent).Name}. Ensure a stream is configured to handle this event type.");
        }

        return result;
    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return eventStorage.LoadEventsAsync<TEvent>(grainId, cancellationToken);
    }

    protected abstract void Configure(IEventStreamBuilder<TEventGrain, TProjection> builder);

    //protected static void Configure<TEventGrain>(Action<IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>> builderAction)
    //    where TEventGrain : IGrain
    //{
    //    var builder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>();
    //    builderAction(builder);
    //}
}
