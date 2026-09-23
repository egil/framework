namespace Egil.Orleans.Messaging.Outboxes;

// A total lives for the meter's lifetime; contributions live only with their activation.
// The total never points back to a contribution or to application objects.
internal sealed class OutboxPendingCounter
{
    private long count;

    public long Count => Interlocked.Read(ref count);

    public Contribution Track(Task deactivated)
    {
        var contribution = new Contribution(this);

        // Registration is also supported after lifecycle startup. Do not capture the
        // activation's ExecutionContext (including AsyncLocals) in this lifetime callback.
        using var flow = ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow();
        _ = deactivated.ContinueWith(
            static (_, state) => ((Contribution)state!).Dispose(),
            contribution,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return contribution;
    }

    internal sealed class Contribution(OutboxPendingCounter total) : IDisposable
    {
        private readonly object gate = new();
        private bool pending;
        private bool closed;

        public void SetPending(bool value)
        {
            // Cleanup runs outside the grain scheduler. Keep the flag and delta in
            // one critical section so cleanup cannot overtake an unfinished increment.
            lock (gate)
            {
                if (closed || pending == value)
                {
                    return;
                }

                Interlocked.Add(ref total.count, value ? 1 : -1);
                pending = value;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed)
                {
                    return;
                }

                closed = true;
                if (pending)
                {
                    Interlocked.Decrement(ref total.count);
                    pending = false;
                }
            }
        }
    }
}
