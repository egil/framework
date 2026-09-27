using System.Collections.Concurrent;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxDeactivationProbe
{
    private readonly ConcurrentDictionary<GrainId, TaskCompletionSource> entries = new();
    private readonly ConcurrentDictionary<GrainId, Task> completions = new();

    public Task WaitForCompletionAsync(GrainId grainId) => completions[grainId];

    public Task WaitForEntryAsync(GrainId grainId) => Entry(grainId).Task;

    public void Observe(IGrainContext context)
    {
        completions[context.GrainId] = context.Deactivated;
        var component = context.GetComponent<IOutboxComponent>()
            ?? throw new InvalidOperationException("No outbox component is attached.");
        context.SetComponent<IOutboxComponent>(new ObservedComponent(component, Entry(context.GrainId)));
    }

    private TaskCompletionSource Entry(GrainId grainId) => entries.GetOrAdd(grainId,
        static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    private sealed class ObservedComponent(IOutboxComponent inner, TaskCompletionSource entered) : IOutboxComponent
    {
        public Task OnActivateAsync(CancellationToken cancellationToken) => inner.OnActivateAsync(cancellationToken);

        public ValueTask ReceiveReminderAsync(string reminderName, TickStatus status) =>
            inner.ReceiveReminderAsync(reminderName, status);

        public Task OnDeactivateAsync(CancellationToken cancellationToken)
        {
            // Signalling from the grain's OnDeactivateAsync would be too early:
            // the processor's lifecycle hook runs afterwards. Invoke it first so
            // the test releases storage only after the real hook reaches its await.
            var deactivation = inner.OnDeactivateAsync(cancellationToken);
            entered.TrySetResult();
            return deactivation;
        }
    }
}
