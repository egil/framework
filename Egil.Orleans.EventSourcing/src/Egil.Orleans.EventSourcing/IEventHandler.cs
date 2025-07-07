namespace Egil.Orleans.EventSourcing;

public interface IEventHandler
{
}

/// <summary>
/// Interface for event handlers.
/// </summary>
public interface IEventHandler<TEvent, TProjection> : IEventHandler
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventGrainContext context);
}

public interface IEventPublisher
{
    bool CanPublish<TEvent>(TEvent @event) where TEvent : notnull;
}

public interface IEventPublisher<TEvent, TProjection> : IEventPublisher
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask PublishAsync(IEnumerable<TEvent> @event, TProjection projection, IEventGrainContext context);
}
