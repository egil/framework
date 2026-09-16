using System.Collections.Immutable;
using System.Diagnostics;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>Constructs fresh outboxes from payload collection expressions.</summary>
public static class Outbox
{
    /// <summary>Enqueues the supplied payloads in a fresh outbox, assigning consecutive message IDs.</summary>
    /// <remarks>
    /// Collection expressions create new history, even when spreading an existing outbox.
    /// Use AddRange to extend existing history and Clear to drain it without resetting it.
    /// All payloads in this construction share one system UTC timestamp.
    /// </remarks>
    public static Outbox<T> Create<T>(ReadOnlySpan<T> messages)
    {
        if (messages.IsEmpty)
        {
            return Outbox<T>.Create();
        }

        var now = TimeProvider.System.GetUtcNow();
        // Captured here, not at delivery time. The processor drains on a grain
        // timer, a reminder, or whichever request happens to trigger the drain,
        // and dispatches groups concurrently, so Activity.Current during delivery
        // is unrelated to the request that appended this message.
        var traceParent = Activity.Current?.Id;
        var builder = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(messages.Length);
        foreach (var message in messages)
        {
            var id = new OutboxMessageId(builder.Count + 1L, now, now, traceParent);
            builder.Add(new(id, message));
        }

        return new(messages.Length, builder.MoveToImmutable(), now, Guid.CreateVersion7());
    }
}
