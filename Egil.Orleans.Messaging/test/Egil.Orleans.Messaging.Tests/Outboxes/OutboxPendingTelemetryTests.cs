using System.Diagnostics.Metrics;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxPendingTelemetryTests(MessagingTestClusterFixture fixture)
    : IClassFixture<MessagingTestClusterFixture>, IAsyncLifetime
{
    private readonly List<IOutboxTelemetryGrain> grains = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await Task.WhenAll(grains.Select(grain => grain.SetPendingAsync(0)));

    private IOutboxTelemetryGrain CreateGrain()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxTelemetryGrain>(Guid.NewGuid());
        grains.Add(grain);
        return grain;
    }

    [Fact]
    public async Task Pending_count_tracks_activations_and_is_available_to_late_listeners()
    {
        var first = CreateGrain();
        var second = CreateGrain();
        await first.SetPendingAsync(5);
        await second.SetPendingAsync(0);
        using var metrics = new PendingOutboxMeasurements(nameof(OutboxTelemetryGrain));
        Assert.Equal(1, metrics.Collect());

        await first.SetPendingAsync(3);
        Assert.Equal(1, metrics.Collect());
        await second.SetPendingAsync(2);
        Assert.Equal(2, metrics.Collect());

        await first.SetPendingAsync(0);
        Assert.Equal(1, metrics.Collect());
        await first.SetPendingAsync(0);
        Assert.Equal(1, metrics.Collect());
        await second.SetPendingAsync(0);
        Assert.Equal(0, metrics.Collect());
        metrics.Dispose();
        await first.SetPendingAsync(1);
        using var reconnected = new PendingOutboxMeasurements(nameof(OutboxTelemetryGrain));
        Assert.Equal(1, reconnected.Collect());
    }

    [Fact]
    public async Task Deactivation_removes_pending_count_and_reactivation_can_count_again()
    {
        var grain = CreateGrain();
        await grain.SetPendingAsync(1);
        using var metrics = new PendingOutboxMeasurements(nameof(OutboxTelemetryGrain));
        Assert.Equal(1, metrics.Collect());

        await grain.DeactivateAsync();
        await metrics.WaitForCountAsync(0);

        await grain.SetPendingAsync(1);
        Assert.Equal(1, metrics.Collect());
    }

    [Fact]
    public async Task Empty_background_observation_removes_pending_count()
    {
        var grain = CreateGrain();
        await grain.SetPendingAsync(1);
        using var metrics = new PendingOutboxMeasurements(nameof(OutboxTelemetryGrain));
        Assert.Equal(1, metrics.Collect());

        await grain.ClearInBackgroundAsync();

        Assert.Equal(0, metrics.Collect());
    }

    [Fact]
    public async Task Acknowledgement_that_clears_then_throws_does_not_leave_stale_pending_count()
    {
        var grain = CreateGrain();
        await grain.SetPendingAsync(1);
        using var metrics = new PendingOutboxMeasurements(nameof(OutboxTelemetryGrain));
        Assert.Equal(1, metrics.Collect());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ClearThenThrowAsync());

        Assert.Equal("acknowledgement failed", error.Message);
        Assert.Equal(0, metrics.Collect());
    }

    [Fact]
    public async Task Constructor_registration_is_counted_and_host_shutdown_cleans_it_up()
    {
        await using var cluster = new MessagingTestClusterFixture();
        await cluster.InitializeAsync();
        var grain = cluster.GrainFactory.GetGrain<IConstructorOutboxTelemetryGrain>(Guid.NewGuid());
        await grain.SetPendingAsync(1);
        using var metrics = new PendingOutboxMeasurements(nameof(ConstructorOutboxTelemetryGrain));
        Assert.Equal(1, metrics.Collect());

        await cluster.Cluster.StopAllSilosAsync(TestContext.Current.CancellationToken);

        await metrics.WaitForCountAsync(0);
    }

    [Fact]
    public async Task Failed_activation_cleans_up_its_observed_backlog()
    {
        var grain = fixture.GrainFactory.GetGrain<IFailingOutboxTelemetryGrain>(Guid.NewGuid());
        using var metrics = new PendingOutboxMeasurements(nameof(FailingOutboxTelemetryGrain));

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ActivateAsync());

        await metrics.WaitForCountAsync(0);
    }
}

internal sealed class PendingOutboxMeasurements : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<long> values = [];

    public PendingOutboxMeasurements(string grainType)
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "egil.orleans.messaging" && instrument.Name == "outbox.grains.pending")
            {
                Assert.IsType<ObservableUpDownCounter<long>>(instrument);
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "grain.type" && Equals(tag.Value, grainType))
                {
                    values.Add(value);
                }
            }
        });
        listener.Start();
    }

    public long Collect()
    {
        values.Clear();
        listener.RecordObservableInstruments();
        return Assert.Single(values);
    }

    public async Task WaitForCountAsync(long expected)
    {
        // Observable metrics have no push notification when their backing total changes.
        // Sample as an exporter would while Orleans finishes asynchronous deactivation.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var scrapes = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (Collect() != expected)
        {
            await scrapes.WaitForNextTickAsync(deadline.Token);
        }
    }

    public void Dispose() => listener.Dispose();
}

public interface IOutboxTelemetryGrain : IGrainWithGuidKey
{
    Task SetPendingAsync(int count);
    Task ClearInBackgroundAsync();
    Task ClearThenThrowAsync();
    Task DeactivateAsync();
}

public sealed class OutboxTelemetryGrain : Grain, IOutboxTelemetryGrain, IOutboxGrain
{
    private Outbox<string> outbox = [];
    private OutboxProcessor<string>? processor;
    private bool throwAfterClearing;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<string>
        {
            OutboxAccessor = () => outbox,
            AcknowledgePosted = _ =>
            {
                if (throwAfterClearing)
                {
                    outbox = outbox.Clear();
                    throwAfterClearing = false;
                    throw new InvalidOperationException("acknowledgement failed");
                }
            },
            RetryDelay = TimeSpan.FromHours(1)
        }).AddPostman<string>(static _ => ValueTask.CompletedTask);
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task SetPendingAsync(int count)
    {
        throwAfterClearing = false;
        outbox = outbox.Clear().AddRange(Enumerable.Repeat("pending", count));
        await processor!.PostAsync();
    }
    public async Task ClearInBackgroundAsync()
    {
        outbox = outbox.Clear();
        await processor!.PostInBackgroundAsync();
    }

    public async Task ClearThenThrowAsync()
    {
        throwAfterClearing = true;
        await processor!.PostAsync();
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

public interface IConstructorOutboxTelemetryGrain : IGrainWithGuidKey
{
    Task SetPendingAsync(int count);
}

public sealed class ConstructorOutboxTelemetryGrain : Grain, IConstructorOutboxTelemetryGrain, IOutboxGrain
{
    private Outbox<string> outbox = [];
    private readonly OutboxProcessor<string> processor;

    public ConstructorOutboxTelemetryGrain()
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<string>
        {
            OutboxAccessor = () => outbox,
            AcknowledgePosted = _ => { },
            RetryDelay = TimeSpan.FromHours(1)
        }).AddPostman<string>(static _ => ValueTask.CompletedTask);
    }

    public async Task SetPendingAsync(int count)
    {
        outbox = outbox.Clear().AddRange(Enumerable.Repeat("pending", count));
        await processor.PostAsync();
    }
}

public interface IFailingOutboxTelemetryGrain : IGrainWithGuidKey
{
    Task ActivateAsync();
}

public sealed class FailingOutboxTelemetryGrain : Grain, IFailingOutboxTelemetryGrain, IOutboxGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<string>
        {
            OutboxAccessor = static () => ["pending"],
            AcknowledgePosted = _ => { },
            RetryDelay = TimeSpan.FromHours(1)
        }).AddPostman<string>(static _ => ValueTask.CompletedTask);
        await processor.PostInBackgroundAsync(cancellationToken);
        throw new InvalidOperationException("activation failed");
    }

    public Task ActivateAsync() => Task.CompletedTask;
}