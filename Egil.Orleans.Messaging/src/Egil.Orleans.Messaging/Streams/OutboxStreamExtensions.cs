using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;

namespace Orleans.Streams;

/// <summary>Publishes plain stream events with stable outbox delivery identity.</summary>
public static class OutboxStreamExtensions
{
    /// <summary>Publishes one logical event, retaining its identity across outbox retries.</summary>
    /// <remarks>
    /// Await publication before acknowledging the outbox item. Use separate outbox entries for
    /// distinct events on the same stream. The token is transport metadata, never a native stream position.
    /// </remarks>
    public static async Task PublishFromOutboxAsync<T>(this IAsyncStream<T> stream, T message, OutboxSequenceToken outboxToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(outboxToken);
        var encoded = OutboxStreamContext.Encode(outboxToken);
        var hadPrevious = RequestContext.Keys.Contains(OutboxStreamContext.Key);
        var previous = RequestContext.Get(OutboxStreamContext.Key);
        RequestContext.Set(OutboxStreamContext.Key, encoded);
        try
        {
            // Keep this scope inside an async method: sibling publications must not share an identity.
            await stream.OnNextAsync(message);
        }
        finally
        {
            if (hadPrevious)
                RequestContext.Set(OutboxStreamContext.Key, previous!);
            else
                RequestContext.Remove(OutboxStreamContext.Key);
        }
    }
}
