using Egil.Orleans.Messaging.Outboxes;

namespace Egil.Orleans.Messaging.Streams;

internal static class OutboxStreamContext
{
    internal const string Key = "egil.orleans.messaging.outbox";

    internal static IDisposable Attach(OutboxSequenceToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        Validate(token);
        return new Scope(token);
    }

    internal static OutboxSequenceToken? Detach()
    {
        var token = Read();
        RequestContext.Remove(Key);
        return token;
    }

    internal static OutboxSequenceToken? Read()
    {
        if (!RequestContext.Keys.Contains(Key))
            return null;

        // Orleans transports the typed value alongside the event. A present invalid entry,
        // including the old JSON string format, must fault rather than silently weaken deduplication.
        if (RequestContext.Get(Key) is not OutboxSequenceToken token || !HasValidIdentity(token))
            throw new InvalidOperationException("Request context must contain an OutboxSequenceToken with a non-default sender and a positive sequence number.");
        return token;
    }

    internal static void Validate(OutboxSequenceToken token)
    {
        if (!HasValidIdentity(token))
            throw new ArgumentException("Outbox identity requires a non-default sender and a positive sequence number.", nameof(token));
    }

    private static bool HasValidIdentity(OutboxSequenceToken token) => !token.Sender.IsDefault && token.SequenceNumber > 0;

    private sealed class Scope : IDisposable
    {
        private readonly bool hadPrevious = RequestContext.Keys.Contains(Key);
        private readonly object? previous = RequestContext.Get(Key);
        private bool disposed;

        public Scope(OutboxSequenceToken token) => RequestContext.Set(Key, token);

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            // Restore only our entry: unrelated context changes made inside the scope belong to the caller.
            if (hadPrevious)
                RequestContext.Set(Key, previous!);
            else
                RequestContext.Remove(Key);
        }
    }
}
