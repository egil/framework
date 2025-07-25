using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing.Reactors;

internal class EventReactorFunctionWrapper<TEvent, TProjection> : IEventReactor<TEvent, TProjection>, IEventReactor<TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    private readonly Func<IEnumerable<TEvent>, TProjection, IEventReactContext, CancellationToken, ValueTask> reactorFunction;

    public string Id { get; }

    public EventReactorFunctionWrapper(string id, Func<IEnumerable<TEvent>, TProjection, ValueTask> reactorFunction)
        : this(id, (e, p, c, ct) => { reactorFunction.Invoke(e, p); return ValueTask.CompletedTask; })
    {
    }


    public EventReactorFunctionWrapper(string id, Func<IEnumerable<TEvent>, TProjection, IEventReactContext, CancellationToken, ValueTask> reactorFunction)
    {
        this.reactorFunction = reactorFunction;
        Id = id;
    }

    public ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        => reactorFunction.Invoke(@event, projection, context, cancellationToken);

    public ValueTask ReactAsync(IEnumerable<IEventEntry> eventEntries, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
    {
        return ReactAsync(
            eventEntries.OfType<IEventEntry<TEvent>>().Select(x => x.Event),
            projection,
            context,
            cancellationToken);
    }

    public bool Matches<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return true;
        }

        return false;
    }

}
