using System.Collections.Immutable;
using Egil.Orleans.Messaging.Outboxes;

namespace Egil.Orleans.Messaging.Journaling;

/// <summary>
/// The outbox's collection and mutation API over a registered journal component.
/// Mutations stage changes and return this component so fluent calls remain journaled.
/// AsImmutable supplies the internally held immutable value for processor access.
/// </summary>
public interface IDurableOutbox<T> : IReadOnlyList<T>
{
    /// <summary>Returns the internally held immutable value without copying, including unsaved changes.</summary>
    Outbox<T> AsImmutable();
    /// <summary>Gets the immutable value revision, which changes when the outbox changes.</summary>
    Guid Revision { get; }
    /// <summary>Gets the highest assigned sequence number, including removed messages.</summary>
    long LatestSequenceNumber { get; }
    /// <summary>Gets the first append instant, or null before any message has been added.</summary>
    DateTimeOffset? Epoch { get; }
    /// <summary>Gets whether no messages are pending.</summary>
    bool IsEmpty { get; }
    /// <summary>Gets an immutable view of pending messages and their assigned identities.</summary>
    ImmutableArray<OutboxMessageEnvelope<T>> Envelopes { get; }
    /// <summary>Appends a message with a new identity and returns this component.</summary>
    IDurableOutbox<T> Add(T message);
    /// <summary>Appends a message with a new identity and returns this component.</summary>
    IDurableOutbox<T> Add(T message, DateTimeOffset utcNow);
    /// <summary>Appends the entire batch in enumeration order and returns this component.</summary>
    IDurableOutbox<T> AddRange(IEnumerable<T> messages);
    /// <summary>Appends the entire batch in enumeration order and returns this component.</summary>
    IDurableOutbox<T> AddRange(IEnumerable<T> messages, DateTimeOffset utcNow);
    /// <summary>Removes the matching FIFO head, if any, and returns this component.</summary>
    IDurableOutbox<T> Remove(OutboxMessageEnvelope<T> item);
    /// <summary>Removes matching queued identities, preserving remaining order, and returns this component.</summary>
    IDurableOutbox<T> RemoveRange(IEnumerable<OutboxMessageEnvelope<T>> items);
    /// <summary>Removes the matching FIFO head, if any, and returns this component.</summary>
    IDurableOutbox<T> Remove(OutboxMessageId id);
    /// <summary>Removes matching queued identities, preserving remaining order, and returns this component.</summary>
    IDurableOutbox<T> RemoveRange(IEnumerable<OutboxMessageId> ids);
    /// <summary>Removes pending messages, preserves sequence history, and returns this component.</summary>
    IDurableOutbox<T> Clear();
}
