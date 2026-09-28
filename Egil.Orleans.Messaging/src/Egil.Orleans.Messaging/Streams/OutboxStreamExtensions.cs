using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;

namespace Orleans.Streams;

/// <summary>Scopes and reads outbox delivery identity, and publishes plain stream events with that identity.</summary>
public static class OutboxStreamExtensions
{
    extension(RequestContext)
    {
        /// <summary>Attaches an outbox token to the current request context until the returned scope is disposed.</summary>
        /// <remarks>
        /// Use a <c>using</c> block in the async postman and await every publication before it completes.
        /// Open the scope around iterator enumeration, not across <c>yield return</c> inside an iterator.
        /// Grain calls and asynchronous work started inside also inherit the token; disposal does not revoke
        /// context already captured by that work. Dispose nested scopes in reverse order in the same logical flow.
        /// One token identifies one logical event per provider and full stream ID.
        /// Unrelated events published by a called grain inherit that identity too; a receiver can discard
        /// distinct events on the same stream as duplicates. Use <see cref="DetachOutboxToken"/> to read and
        /// remove the incoming token before starting unrelated downstream work.
        /// The token is transported as a typed value using the provider's serializer; participating endpoints
        /// must have the generated OutboxSequenceToken serializer available.
        /// </remarks>
        /// <returns>A scope that restores the previous entry, including its absence, without changing other entries.</returns>
        /// <exception cref="ArgumentNullException">The token is null.</exception>
        /// <exception cref="ArgumentException">The token has a default sender or a non-positive sequence number.</exception>
        public static IDisposable AttachOutboxToken(OutboxSequenceToken token) => OutboxStreamContext.Attach(token);

        /// <summary>Reads the outbox token from the current request context, or returns <c>null</c> when absent.</summary>
        /// <remarks>
        /// Reading leaves the context unchanged. Present invalid metadata faults delivery rather than bypassing
        /// deduplication. StreamManager application handlers read their token from <see cref="StreamCursor.OutboxToken"/>
        /// because the manager masks ambient identity while invoking them.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The entry is present but null, of the wrong type, or has an invalid identity.</exception>
        public static OutboxSequenceToken? GetOutboxToken() => OutboxStreamContext.Read();

        /// <summary>Reads and removes the outbox token from the current request context, or returns <c>null</c> when absent.</summary>
        /// <remarks>
        /// Detach before starting unrelated publications or grain calls so they do not inherit the delivered
        /// event's identity. This removes the entry from the current logical flow without creating a restoration
        /// scope; it cannot change context captured by work already started. Invalid metadata is left unchanged.
        /// StreamManager detaches before invoking application handlers, which use <see cref="StreamCursor.OutboxToken"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The entry is present but null, of the wrong type, or has an invalid identity.</exception>
        public static OutboxSequenceToken? DetachOutboxToken() => OutboxStreamContext.Detach();

    }

    /// <summary>Publishes one logical event, retaining its identity across outbox retries.</summary>
    /// <remarks>
    /// Await publication before acknowledging the outbox item. Use separate outbox entries for
    /// distinct events on the same stream. The token is transport metadata, never a native stream position.
    /// </remarks>
    public static async Task PublishFromOutboxAsync<T>(this IAsyncStream<T> stream, T message, OutboxSequenceToken outboxToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(outboxToken);
        // Keep this scope inside an async method: sibling publications must not share an identity.
        using var scope = RequestContext.AttachOutboxToken(outboxToken);
        await stream.OnNextAsync(message);
    }
}
