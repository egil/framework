namespace Egil.Orleans.Messaging.Outboxes;

internal readonly record struct OutboxDispatchResult<TOutbox>(
    TOutbox Item,
    Exception? Error = null)
    where TOutbox : notnull;