namespace Egil.Orleans.EventSourcing;

public partial interface IEventStreamConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull
{
    /// <summary>
    /// Stream only keeps the <typeparamref name="TEventBase"/> until all handlers and reactors have processed it successfully.
    /// </summary>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepUntilProcessed();

    /// <summary>
    /// Stream keeps the latest <paramref name="count"/> <typeparamref name="TEventBase"/> events.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the stream.
    /// </remarks>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepLast(int count);

    /// <summary>
    /// Stream keeps the <typeparamref name="TEventBase"/> events until their <paramref name="eventTimestampSelector"/> is older than <paramref name="maxAge"/>.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the stream.
    /// </remarks>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepUntil(TimeSpan maxAge, Func<TEventBase, DateTimeOffset> eventTimestampSelector);

    /// <summary>
    /// Stream keeps the distinct <typeparamref name="TEventBase"/> events based on the <paramref name="eventKeySelector"/>.
    /// </summary>
    /// <remarks>
    /// This setting can be used together with the other "Keep" methods to control how many events are retained in the stream.
    /// </remarks>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> KeepDistinct(Func<TEventBase, string> eventKeySelector);

    /// <summary>
    /// Registers a handler factory that creates an event handler instance for processing events.
    /// </summary>
    /// <param name="handlerFactory">Factory function that creates an event handler from the grain instance.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventGrain, IEventHandler<TEventBase, TProjection>> handlerFactory);

    /// <summary>
    /// Registers a handler factory for a specific event type derived from <typeparamref name="TEventBase"/>.
    /// </summary>
    /// <typeparam name="TEvent">The specific event type to handle.</typeparam>
    /// <param name="handlerFactory">Factory function that creates an event handler from the grain instance.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory)
        where TEvent : notnull, TEventBase;

    /// <summary>
    /// Registers a handler factory that returns a function for processing a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The specific event type to handle.</typeparam>
    /// <param name="handlerFactory">Factory function that creates a handler function from the grain instance.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEventGrain, Func<TEvent, TProjection, TProjection>> handlerFactory) where TEvent : TEventBase;

    /// <summary>
    /// Registers an event handler type that will be resolved from the dependency injection container.
    /// </summary>
    /// <typeparam name="TEventHandler">The event handler type to register.</typeparam>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEventHandler>() where TEventHandler : IEventHandler<TEventBase, TProjection>;

    /// <summary>
    /// Registers a handler function for processing events of type <typeparamref name="TEventBase"/>.
    /// </summary>
    /// <param name="handler">Function that processes an event and updates the projection.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle(Func<TEventBase, TProjection, TProjection> handler);

    /// <summary>
    /// Registers a handler function for processing events of a specific type derived from <typeparamref name="TEventBase"/>.
    /// </summary>
    /// <typeparam name="TEvent">The specific event type to handle.</typeparam>
    /// <param name="handler">Function that processes an event and updates the projection.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> Handle<TEvent>(Func<TEvent, TProjection, TProjection> handler) where TEvent : TEventBase;

    /// <summary>
    /// Registers a reactor factory that creates an event reactor instance for reacting to events with side effects.
    /// </summary>
    /// <param name="reactorFactory">Factory function that creates an event reactor from the grain instance.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> React(Func<TEventGrain, IEventReactor<TEventBase, TProjection>> reactorFactory);

    /// <summary>
    /// Registers a reactor factory for a specific event type derived from <typeparamref name="TEventBase"/>.
    /// </summary>
    /// <typeparam name="TEvent">The specific event type to react to.</typeparam>
    /// <param name="reactorFactory">Factory function that creates an event reactor from the grain instance.</param>
    /// <returns>The configurator for method chaining.</returns>
    IEventStreamConfigurator<TEventGrain, TEventBase, TProjection> React<TEvent>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> reactorFactory)
        where TEvent : notnull, TEventBase;
}