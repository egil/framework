using System.Runtime.CompilerServices;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxPendingCounterTests
{
    [Fact]
    public void Deactivation_removes_only_its_contribution_and_closes_it_permanently()
    {
        var total = new OutboxPendingCounter();
        var lifetime = new TaskCompletionSource();
        using var old = total.Track(lifetime.Task);
        using var replacement = total.Track(Task.CompletedTask);
        old.SetPending(true);
        Assert.Equal(1, total.Count);

        lifetime.SetResult();
        old.SetPending(true);
        old.Dispose();
        replacement.SetPending(true);

        Assert.Equal(0, total.Count);
    }

    [Fact]
    public void One_activation_cannot_remove_another_activations_contribution()
    {
        var total = new OutboxPendingCounter();
        var firstLifetime = new TaskCompletionSource();
        var secondLifetime = new TaskCompletionSource();
        using var first = total.Track(firstLifetime.Task);
        using var second = total.Track(secondLifetime.Task);
        first.SetPending(true);
        second.SetPending(true);
        Assert.Equal(2, total.Count);

        firstLifetime.SetResult();
        first.Dispose();
        first.SetPending(true);
        Assert.Equal(1, total.Count);

        second.SetPending(false);
        secondLifetime.SetResult();
        Assert.Equal(0, total.Count);
    }

    [Fact]
    public async Task Concurrent_observation_and_cleanup_leave_no_contribution()
    {
        var total = new OutboxPendingCounter();
        using var contribution = total.Track(new TaskCompletionSource().Task);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = Task.Run(async () =>
        {
            await start.Task;
            contribution.SetPending(true);
        }, TestContext.Current.CancellationToken);
        var close = Task.Run(async () =>
        {
            await start.Task;
            contribution.Dispose();
        }, TestContext.Current.CancellationToken);

        start.SetResult();
        await Task.WhenAll(update, close).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, total.Count);
        contribution.SetPending(true);
        Assert.Equal(0, total.Count);
    }

    [Fact]
    public void Tracking_preserves_caller_owned_flow_suppression_and_cleans_up()
    {
        var total = new OutboxPendingCounter();
        var lifetime = new TaskCompletionSource();
        Assert.False(ExecutionContext.IsFlowSuppressed());

        using (ExecutionContext.SuppressFlow())
        {
            using var contribution = total.Track(lifetime.Task);
            Assert.True(ExecutionContext.IsFlowSuppressed());
            contribution.SetPending(true);
            Assert.Equal(1, total.Count);

            lifetime.SetResult();

            Assert.Equal(0, total.Count);
            Assert.True(ExecutionContext.IsFlowSuppressed());
            contribution.SetPending(true);
            Assert.Equal(0, total.Count);
        }

        Assert.False(ExecutionContext.IsFlowSuppressed());
    }

    [Fact]
    public void Tracking_does_not_capture_application_objects_from_execution_context()
    {
        var total = new OutboxPendingCounter();
        var lifetime = new TaskCompletionSource();
        var payload = TrackWithAmbientPayload(total, lifetime.Task);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(payload.IsAlive);
        Assert.Equal(1, total.Count);
        lifetime.SetResult();
        Assert.Equal(0, total.Count);
    }

    [Fact]
    public void Total_does_not_retain_closed_contributions_after_activation_churn()
    {
        var total = new OutboxPendingCounter();
        var last = Churn(total);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(last.IsAlive);
        Assert.Equal(0, total.Count);
        GC.KeepAlive(total);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackWithAmbientPayload(OutboxPendingCounter total, Task lifetime)
    {
        var ambient = new AsyncLocal<object?> { Value = new byte[1024] };
        var payload = new WeakReference(ambient.Value);
        total.Track(lifetime).SetPending(true);
        ambient.Value = null;
        return payload;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Churn(OutboxPendingCounter total)
    {
        WeakReference last = null!;
        for (var i = 0; i < 1000; i++)
        {
            var lifetime = new TaskCompletionSource();
            var contribution = total.Track(lifetime.Task);
            contribution.SetPending(true);
            lifetime.SetResult();
            last = new WeakReference(contribution);
        }

        return last;
    }
}
