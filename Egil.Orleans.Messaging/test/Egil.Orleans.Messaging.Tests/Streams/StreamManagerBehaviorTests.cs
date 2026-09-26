using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerBehaviorTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(SubscriptionKind.Implicit)]
    [InlineData(SubscriptionKind.Convention)]
    [InlineData(SubscriptionKind.ExplicitId)]
    public async Task Rejected_duplicate_keeps_original_subscription_handler(SubscriptionKind subscriptionKind)
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var result = await grain.DuplicateAsync(subscriptionKind);

        Assert.True(result.Rejected);
        Assert.Equal(["original"], result.Delivered);
    }

    [Theory]
    [InlineData("00-11111111111111111111111111111111-2222222222222222-01", true)]
    [InlineData("invalid-traceparent", false)]
    public async Task Consumer_tracing_links_valid_context_and_records_handler_failure(string traceParent, bool expectedLink)
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "egil.orleans.messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("messaging.destination.name"), streamNamespace))
                {
                    activities.Enqueue(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var result = await grain.DeliverAsync(streamNamespace, false, traceParent);

        Assert.Equal(["after-failure"], result.Delivered);
        Assert.Equal(2, activities.Count);
        var failed = Assert.Single(activities, activity => activity.Status == ActivityStatusCode.Error);
        Assert.Equal("Delivery failed.", failed.StatusDescription);
        Assert.All(activities, activity =>
        {
            Assert.Equal(ActivityKind.Consumer, activity.Kind);
            Assert.Equal(expectedLink ? 1 : 0, activity.Links.Count());
        });
        if (expectedLink)
        {
            var link = Assert.Single(failed.Links);
            Assert.Equal("11111111111111111111111111111111", link.Context.TraceId.ToString());
            Assert.Equal("2222222222222222", link.Context.SpanId.ToString());
        }
    }

    [Theory]
    [InlineData(MessageTraceMode.Link, 0, false)]
    [InlineData(MessageTraceMode.Parent, null, true)]
    [InlineData(MessageTraceMode.Parent, 3600, true)]
    [InlineData(MessageTraceMode.ParentWithinLag, 29, true)]
    [InlineData(MessageTraceMode.ParentWithinLag, 30, true)]
    [InlineData(MessageTraceMode.ParentWithinLag, 31, false)]
    [InlineData(MessageTraceMode.ParentWithinLag, null, false)]
    public async Task Consumer_span_joins_or_links_producer_trace_by_trace_mode(
        MessageTraceMode mode, int? lagSeconds, bool expectParent)
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        using var spans = new ConsumerSpanCollector(streamNamespace);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        await grain.DeliverTracedAsync(streamNamespace, mode, TimeSpan.FromSeconds(30), ProducerTraceParent, lagSeconds, TraceClock.Subscription);

        var span = Assert.Single(spans.Stopped);
        if (expectParent)
        {
            Assert.Equal(ProducerTraceId, span.TraceId.ToString());
            Assert.Equal(ProducerSpanId, span.ParentSpanId.ToString());
            Assert.Empty(span.Links);
        }
        else
        {
            Assert.NotEqual(ProducerTraceId, span.TraceId.ToString());
            var link = Assert.Single(span.Links);
            Assert.Equal(ProducerTraceId, link.Context.TraceId.ToString());
            Assert.Equal(ProducerSpanId, link.Context.SpanId.ToString());
        }
    }

    [Theory]
    [InlineData(TraceClock.Subscription)]
    [InlineData(TraceClock.Services)]
    public async Task Lag_is_measured_with_the_subscription_clock_or_else_the_registered_clock(TraceClock clock)
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        using var spans = new ConsumerSpanCollector(streamNamespace);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        await grain.DeliverTracedAsync(streamNamespace, MessageTraceMode.ParentWithinLag, TimeSpan.FromSeconds(30), ProducerTraceParent, 29, clock);

        var span = Assert.Single(spans.Stopped);
        Assert.Equal(ProducerTraceId, span.TraceId.ToString());
    }

    [Theory]
    [InlineData(MessageTraceMode.Link, 0)]
    [InlineData(MessageTraceMode.ParentWithinLag, 31)]
    public async Task Linked_span_starts_its_own_trace_under_an_ambient_activity(MessageTraceMode mode, int lagSeconds)
    {
        const string ambientTraceId = "33333333333333333333333333333333";
        var streamNamespace = Guid.NewGuid().ToString("N");
        using var spans = new ConsumerSpanCollector(streamNamespace);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var (ambientRestored, _) = await grain.DeliverTracedAsync(
            streamNamespace, mode, TimeSpan.FromSeconds(30), ProducerTraceParent, lagSeconds, TraceClock.Subscription,
            ambientTraceParent: $"00-{ambientTraceId}-4444444444444444-01");

        var span = Assert.Single(spans.Stopped);
        Assert.NotEqual(ambientTraceId, span.TraceId.ToString());
        Assert.NotEqual(ProducerTraceId, span.TraceId.ToString());
        Assert.Equal(default, span.ParentSpanId);
        Assert.Equal(ProducerTraceId, Assert.Single(span.Links).Context.TraceId.ToString());
        Assert.True(ambientRestored);
    }

    [Fact]
    public async Task None_mode_starts_no_span_and_still_records_metrics()
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        using var spans = new ConsumerSpanCollector(streamNamespace);
        long accepted = 0;
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "egil.orleans.messaging" && instrument.Name == "stream.messages")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meters.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "stream.namespace" && Equals(tag.Value, streamNamespace))
                {
                    Interlocked.Add(ref accepted, value);
                }
            }
        });
        meters.Start();
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var (_, handlerSawAmbient) = await grain.DeliverTracedAsync(
            streamNamespace, MessageTraceMode.None, TimeSpan.Zero, ProducerTraceParent, 0, TraceClock.Subscription,
            ambientTraceParent: "00-33333333333333333333333333333333-4444444444444444-01");

        Assert.Empty(spans.Stopped);
        Assert.True(handlerSawAmbient);
        Assert.Equal(1, Interlocked.Read(ref accepted));
    }

    [Fact]
    public async Task Parent_mode_ignores_invalid_producer_traceparent()
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        using var spans = new ConsumerSpanCollector(streamNamespace);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        await grain.DeliverTracedAsync(streamNamespace, MessageTraceMode.Parent, TimeSpan.Zero, "invalid-traceparent", 0, TraceClock.Subscription);

        var span = Assert.Single(spans.Stopped);
        Assert.Empty(span.Links);
        Assert.NotEqual(ProducerTraceId, span.TraceId.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ParentWithinLag_rejects_non_positive_lag(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageTraceOptions.ParentWithinLag(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_error_callback_does_not_prevent_subsequent_delivery(bool callbackThrows)
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());
        var streamNamespace = Guid.NewGuid().ToString("N");

        var result = await grain.DeliverAsync(streamNamespace, callbackThrows, null);

        Assert.Equal(["after-failure"], result.Delivered);
        Assert.Equal(1, result.Errors);
        Assert.True(result.OriginalError);
        Assert.Equal(streamNamespace, result.ErrorNamespace);
    }

    private const string ProducerTraceId = "11111111111111111111111111111111";
    private const string ProducerSpanId = "2222222222222222";
    private const string ProducerTraceParent = $"00-{ProducerTraceId}-{ProducerSpanId}-01";

    private sealed class ConsumerSpanCollector : IDisposable
    {
        private readonly ActivityListener listener;

        public ConsumerSpanCollector(string streamNamespace)
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "egil.orleans.messaging",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "orleans.stream.process"
                        && Equals(activity.GetTagItem("messaging.destination.name"), streamNamespace))
                    {
                        Stopped.Enqueue(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(listener);
        }

        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public void Dispose() => listener.Dispose();
    }
}

public interface IStreamManagerBehaviorGrain : IGrainWithGuidKey
{
    Task<(bool Rejected, string[] Delivered)> DuplicateAsync(SubscriptionKind subscriptionKind);
    Task<(string[] Delivered, int Errors, bool OriginalError, string? ErrorNamespace)> DeliverAsync(string streamNamespace, bool callbackThrows, string? traceParent);
    Task<(bool AmbientRestored, bool HandlerSawAmbient)> DeliverTracedAsync(string streamNamespace, MessageTraceMode mode, TimeSpan maxParentLag, string traceParent, int? lagSeconds, TraceClock clock, string? ambientTraceParent = null);
}

public sealed class StreamManagerBehaviorGrain : Grain, IStreamManagerBehaviorGrain
{
    public async Task<(bool Rejected, string[] Delivered)> DuplicateAsync(SubscriptionKind subscriptionKind)
    {
        var streamId = StreamId.Create("orders", "one");
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", streamId);
        var manager = StreamManagerResumeTests.CreateManager(null, stream, this);
        List<string> delivered = [];
        void Configure(Func<string, StreamCursor, ValueTask> handler)
        {
            switch (subscriptionKind)
            {
                case SubscriptionKind.Implicit:
                    manager.ConfigureImplicitSubscription("orders", handler);
                    break;
                case SubscriptionKind.Convention:
                    manager.ConfigureExplicitSubscription("provider-a", "orders", handler);
                    break;
                default:
                    manager.ConfigureExplicitSubscription("provider-a", streamId, handler);
                    break;
            }
        }
        Configure((message, _) =>
        {
            delivered.Add(message);
            return ValueTask.CompletedTask;
        });
        var rejected = false;
        try
        {
            Configure((_, _) => throw new InvalidOperationException("Replacement handler must not run."));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        if (subscriptionKind == SubscriptionKind.Implicit)
        {
            var handle = new StreamManagerResumeTests.FakeSubscriptionHandle<string>("provider-a", streamId);
            await ((IStreamManagerComponent)manager).OnSubscribedAsync(
                new StreamManagerResumeTests.FakeStreamSubscriptionHandleFactory("provider-a", streamId, handle));
            await handle.DeliverAsync("original");
        }
        else
        {
            await manager.EnsureExplicitSubscriptionsAsync();
            await stream.OnNextAsync("original");
        }
        return (rejected, delivered.ToArray());
    }

    public async Task<(string[] Delivered, int Errors, bool OriginalError, string? ErrorNamespace)> DeliverAsync(
        string streamNamespace, bool callbackThrows, string? traceParent)
    {
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create(streamNamespace, "one"));
        var manager = StreamManagerResumeTests.CreateManager(null, stream, this);
        List<string> delivered = [];
        var failure = new InvalidOperationException("Delivery failed.");
        var errors = 0;
        var originalError = false;
        string? errorNamespace = null;
        manager.ConfigureExplicitSubscription<string>("provider-a", streamNamespace,
            (message, _) =>
            {
                if (message == "fail")
                {
                    throw failure;
                }
                delivered.Add(message);
                return ValueTask.CompletedTask;
            },
            options => options.OnError = (name, error) =>
            {
                errors++;
                errorNamespace = name;
                originalError = ReferenceEquals(error, failure);
                if (callbackThrows)
                {
                    throw new InvalidOperationException("Error callback failed.");
                }
            });
        await manager.EnsureExplicitSubscriptionsAsync();
        await stream.OnNextAsync("fail", new DiagnosticToken(traceParent));
        await stream.OnNextAsync("after-failure", new DiagnosticToken(traceParent));
        return (delivered.ToArray(), errors, originalError, errorNamespace);
    }

    public async Task<(bool AmbientRestored, bool HandlerSawAmbient)> DeliverTracedAsync(
        string streamNamespace, MessageTraceMode mode, TimeSpan maxParentLag, string traceParent, int? lagSeconds, TraceClock clock, string? ambientTraceParent = null)
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var wrongClock = new ManualTimeProvider(now.AddDays(1));
        Activity? handlerActivity = null;
        var traceOptions = mode switch
        {
            MessageTraceMode.Parent => MessageTraceOptions.Parent,
            MessageTraceMode.ParentWithinLag => MessageTraceOptions.ParentWithinLag(maxParentLag),
            MessageTraceMode.None => MessageTraceOptions.None,
            _ => MessageTraceOptions.Link,
        };
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create(streamNamespace, "one"));
        var manager = StreamManagerResumeTests.CreateManager(
            null,
            stream,
            this,
            defaultTimeProvider: clock == TraceClock.Services ? new ManualTimeProvider(now) : wrongClock);
        manager.ConfigureExplicitSubscription<string>(
            "provider-a",
            streamNamespace,
            (_, _) =>
            {
                handlerActivity = Activity.Current;
                return ValueTask.CompletedTask;
            },
            options =>
            {
                options.Trace = traceOptions;
                options.TimeProvider = clock == TraceClock.Subscription ? new ManualTimeProvider(now) : null;
            });
        await manager.EnsureExplicitSubscriptionsAsync();

        DateTimeOffset? enqueued = lagSeconds is { } lag ? now - TimeSpan.FromSeconds(lag) : null;
        using var ambient = ambientTraceParent is null ? null : new Activity("ambient").SetParentId(ambientTraceParent).Start();
        await stream.OnNextAsync("message", new DiagnosticToken(traceParent, enqueued));
        return (ReferenceEquals(Activity.Current, ambient), ambient is not null && ReferenceEquals(handlerActivity, ambient));
    }
}

internal sealed class DiagnosticToken(string? traceParent, DateTimeOffset? enqueuedTime = null) : EventSequenceToken(1), IStreamSequenceTokenMetadata
{
    public bool TryGetTraceParent([NotNullWhen(true)] out string? value)
    {
        value = traceParent;
        return value is not null;
    }

    public bool TryGetEnqueuedTime(out DateTimeOffset value)
    {
        value = enqueuedTime.GetValueOrDefault();
        return enqueuedTime.HasValue;
    }

    public bool TryGetProviderName([NotNullWhen(true)] out string? value)
    {
        value = "provider-a";
        return true;
    }
}

public enum TraceClock
{
    Subscription,
    Services
}

public enum SubscriptionKind
{
    Implicit,
    Convention,
    ExplicitId
}
