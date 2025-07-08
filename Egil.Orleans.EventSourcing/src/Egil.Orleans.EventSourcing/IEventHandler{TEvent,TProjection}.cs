namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for event handlers.
/// </summary>
public interface IEventHandler<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask<TProjection> HandleAsync<TEvent>(TEvent @event, TProjection projection, IEventHandlerContext context) where TEvent : notnull;
}

public interface IEventHandler<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventHandlerContext context);
}