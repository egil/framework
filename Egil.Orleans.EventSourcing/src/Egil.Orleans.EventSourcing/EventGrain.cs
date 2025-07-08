using Orleans;
using Orleans.Runtime;
using Egil.Orleans.EventSourcing.Internals;

namespace Egil.Orleans.EventSourcing;

internal interface IEventGrain
{
    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default) where TEvent : notnull;
}

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain, IEventGrain
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore eventstore;
    private readonly GrainId grainId;
    private readonly IEventStream<TProjection>[] streams;
    private EventContext? activeContext;

    protected EventGrain(IEventStore eventStorage)
    {
        this.eventstore = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
        var builder = new EventStreamBuilder<TEventGrain, TProjection>();
        Configure(builder);
        streams = builder.Build();
    }

    protected IEventStore EventStorage => eventstore;

    protected TProjection Projection { get; set; }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected async ValueTask ProcessEventAsync<TEvent>(TEvent @event) where TEvent : notnull
    {
        var topScope = activeContext is null;
        activeContext ??= new EventContext(grainId, this, GrainFactory);

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
        var context = new EventContext(grainId, this, GrainFactory);
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

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var streamName = FindStream<TEvent>(default);
        return eventstore.LoadEventsAsync<TEvent>(streamName.Name, grainId, cancellationToken);
    }

    IAsyncEnumerable<TEvent> IEventGrain.GetEventsAsync<TEvent>(CancellationToken cancellationToken) => GetEventsAsync<TEvent>(cancellationToken);

    protected abstract void Configure(IEventStreamBuilder<TEventGrain, TProjection> builder);

    //protected static void Configure<TEventGrain>(Action<IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>> builderAction)
    //    where TEventGrain : IGrain
    //{
    //    var builder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>();
    //    builderAction(builder);
    //}
}
