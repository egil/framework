using System.Collections.Concurrent;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class ActivationCompletions
{
    private readonly ConcurrentDictionary<GrainId, Task> completions = new();
    public void Observe(IGrainContext context) => completions[context.GrainId] = context.Deactivated;
    public Task WaitAsync(GrainId id) => completions[id].WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
