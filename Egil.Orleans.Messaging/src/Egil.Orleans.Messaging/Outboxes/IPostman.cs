namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Delivers one outbox message under the processor-owned cancellation budget.
/// </summary>
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