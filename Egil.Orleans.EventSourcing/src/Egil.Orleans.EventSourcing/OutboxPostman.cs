namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for outbox event postmen that handle publishing specific types of outbox events.
/// </summary>
/// <typeparam name="TOutboxEvent">The type of outbox event this postman handles</typeparam>
public interface IOutboxPostman<in TOutboxEvent>
{
    /// <summary>
    /// Processes an outbox event and publishes it to its destination.
    /// </summary>
    /// <param name="outboxEvent">The outbox event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the event was successfully processed, false if it should be retried</returns>
    ValueTask<bool> ProcessEventAsync(TOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstract base class for outbox postmen.
/// </summary>
/// <typeparam name="TOutboxEvent">The type of outbox event this postman handles</typeparam>
public abstract class OutboxPostman<TOutboxEvent> : IOutboxPostman<TOutboxEvent>
{
    /// <summary>
    /// Processes an outbox event and publishes it to its destination.
    /// </summary>
    /// <param name="outboxEvent">The outbox event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the event was successfully processed, false if it should be retried</returns>
    public abstract ValueTask<bool> ProcessEventAsync(TOutboxEvent outboxEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for outbox postmen that handle publishing outbox events.
/// </summary>
public sealed class OutboxPostmanConfiguration
{
    private readonly Dictionary<Type, Func<object, CancellationToken, ValueTask<bool>>> postmen = new();
    private readonly IServiceProvider? serviceProvider;

    /// <summary>
    /// Initializes a new OutboxPostmanConfiguration.
    /// </summary>
    /// <param name="serviceProvider">Optional service provider for DI-based postmen</param>
    public OutboxPostmanConfiguration(IServiceProvider? serviceProvider = null)
    {
        this.serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Registers a postman for a specific outbox event type.
    /// </summary>
    public OutboxPostmanConfiguration RegisterPostman<TOutboxEvent>(IOutboxPostman<TOutboxEvent> postman)
    {
        var eventType = typeof(TOutboxEvent);
        postmen[eventType] = (evt, ct) => postman.ProcessEventAsync((TOutboxEvent)evt, ct);
        return this;
    }

    /// <summary>
    /// Registers a DI-based postman for a specific outbox event type.
    /// </summary>
    public OutboxPostmanConfiguration RegisterPostman<TOutboxEvent, TPostman>()
        where TPostman : class, IOutboxPostman<TOutboxEvent>
    {
        if (serviceProvider == null)
            throw new InvalidOperationException("Service provider not available for DI-based postmen");

        var eventType = typeof(TOutboxEvent);
        postmen[eventType] = (evt, ct) =>
        {
            var postman = serviceProvider.GetRequiredService<TPostman>();
            return postman.ProcessEventAsync((TOutboxEvent)evt, ct);
        };
        return this;
    }

    /// <summary>
    /// Registers a delegate-based postman for a specific outbox event type.
    /// </summary>
    public OutboxPostmanConfiguration RegisterPostman<TOutboxEvent>(
        Func<TOutboxEvent, CancellationToken, ValueTask<bool>> postmanDelegate)
    {
        var eventType = typeof(TOutboxEvent);
        postmen[eventType] = (evt, ct) => postmanDelegate((TOutboxEvent)evt, ct);
        return this;
    }

    /// <summary>
    /// Gets the configured postmen.
    /// </summary>
    internal IReadOnlyDictionary<Type, Func<object, CancellationToken, ValueTask<bool>>> GetPostmen() => postmen;
}

/// <summary>
/// Service for processing outbox events using registered postmen.
/// </summary>
public sealed class OutboxPostmanService
{
    private readonly IReadOnlyDictionary<Type, Func<object, CancellationToken, ValueTask<bool>>> postmen;

    /// <summary>
    /// Initializes a new OutboxPostmanService.
    /// </summary>
    public OutboxPostmanService(OutboxPostmanConfiguration configuration)
    {
        postmen = configuration.GetPostmen();
    }

    /// <summary>
    /// Processes an outbox event using the appropriate postman.
    /// </summary>
    /// <param name="outboxEvent">The outbox event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if processed successfully, false if should be retried</returns>
    public async ValueTask<bool> ProcessEventAsync(object outboxEvent, CancellationToken cancellationToken = default)
    {
        var eventType = outboxEvent.GetType();
        
        if (postmen.TryGetValue(eventType, out var postman))
        {
            return await postman(outboxEvent, cancellationToken);
        }

        // No postman registered for this event type
        return true; // Consider it processed to avoid infinite loops
    }
}
