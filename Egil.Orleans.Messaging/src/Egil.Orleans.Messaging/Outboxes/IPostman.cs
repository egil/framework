namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Delivers one outbox message under the processor-owned cancellation budget.
/// </summary>
/// <remarks>
/// Implementations are resolved from the grain activation service provider and
/// should be ordinary delivery services: use injected dependencies, the
/// message payload, and the cancellation token. They should not depend on the
/// owning grain's activation-local state. Use inline postman callbacks only
/// when delivery genuinely needs grain-local context.
/// </remarks>
/// <typeparam name="TMessage">The message type handled by this postman.</typeparam>
public interface IPostman<in TMessage>
    where TMessage : notnull
{
    /// <summary>
    /// Posts the message to its target.
    /// </summary>
    /// <param name="message">The message to post.</param>
    /// <param name="cancellationToken">Cancellation token owned by the outbox processor.</param>
    ValueTask PostAsync(TMessage message, CancellationToken cancellationToken);
}