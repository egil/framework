using System.Collections.Concurrent;

namespace Egil.Orleans.Messaging.Journaling.Tests;

// A stand-in for the external transport; delivery records are deliberately outside grain persistence.
public sealed class DeliveredMessages
{
    private readonly ConcurrentDictionary<GrainId, ConcurrentQueue<OrderEvent>> messages = new();
    public Task DeliverAsync(GrainId grain, OrderEvent message)
    {
        messages.GetOrAdd(grain, static _ => new()).Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<OrderEvent> For(GrainId grain) =>
        messages.TryGetValue(grain, out var delivered) ? delivered.ToArray() : [];
}
