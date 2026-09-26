using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxDispatcherTraceContextTests : IDisposable
{
    private readonly ActivitySource source = new("test." + Guid.NewGuid().ToString("N"));

    // Test classes run in parallel and share the egil.orleans.messaging source,
    // so recorded spans are filtered by a grain type unique to this instance.
    private readonly string grainType = "TestGrain-" + Guid.NewGuid().ToString("N");

    private MessageTraceOptions trace = MessageTraceOptions.Link;
    private TimeProvider time = TimeProvider.System;

    public void Dispose() => source.Dispose();

    [Fact]
    public async Task Parent_mode_parents_the_span_to_the_trace_that_added_the_message()
    {
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var addTrace = source.StartActivity("request-a")!;
        var addContext = addTrace.Context;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();
        trace = MessageTraceOptions.Parent;

        using var drainTrace = source.StartActivity("request-b")!;
        await DispatchAsync(envelope);

        var span = Assert.Single(recorded);
        Assert.Equal(addContext.TraceId, span.TraceId);
        Assert.Equal(addContext.SpanId, span.ParentSpanId);
        Assert.Empty(span.Links);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public async Task ParentWithinLag_parents_recent_messages_and_links_older_ones(int ageSeconds, bool expectParent)
    {
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var addTrace = source.StartActivity("request-a")!;
        var addTraceId = addTrace.TraceId;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();
        trace = MessageTraceOptions.ParentWithinLag(TimeSpan.FromSeconds(30));
        time = new ManualTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(ageSeconds));

        await DispatchAsync(envelope);

        var span = Assert.Single(recorded);
        Assert.Equal(expectParent, span.TraceId == addTraceId);
        Assert.Equal(expectParent ? 0 : 1, span.Links.Count());
    }

    [Fact]
    public async Task None_mode_starts_no_span_and_still_records_metrics()
    {
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        long posted = 0;
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "egil.orleans.messaging" && instrument.Name == "outbox.post.items")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meters.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "grain.type" && Equals(tag.Value, grainType))
                {
                    Interlocked.Add(ref posted, value);
                }
            }
        });
        meters.Start();
        var addTrace = source.StartActivity("request-a")!;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();
        trace = MessageTraceOptions.None;
        using var drainTrace = source.StartActivity("request-b")!;
        var observed = new List<Activity?>();

        await DispatchAsync(_ => observed.Add(Activity.Current), envelope);

        Assert.Empty(recorded);
        Assert.Same(drainTrace, Assert.Single(observed));
        Assert.Equal(1, Interlocked.Read(ref posted));
    }

    [Fact]
    public async Task Dispatch_links_each_message_to_the_trace_that_added_it()
    {
        // A drain flushes every pending message under one ambient activity, so a
        // message added by request A but flushed by request B's drain used to be
        // attributed to B. A wrong link is indistinguishable from a right one when
        // reading a trace, which is why this regression needs its own test.
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var addTrace = source.StartActivity("request-a")!;
        var addTraceId = addTrace.TraceId;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();

        using var drainTrace = source.StartActivity("request-b")!;
        await DispatchAsync(envelope);

        var span = Assert.Single(recorded);
        var link = Assert.Single(span.Links);
        Assert.Equal(addTraceId, link.Context.TraceId);
        Assert.NotEqual(drainTrace.TraceId, link.Context.TraceId);
    }

    [Fact]
    public async Task Dispatch_span_never_joins_the_producing_trace()
    {
        // Links, not parent chaining: a message may be delivered hours after the
        // producing request ended, and re-parenting into an exported trace
        // produces orphaned spans and traces that span the whole delay. The span
        // still joins the trace that drove the drain when there is one — that
        // request genuinely caused this delivery, and it is happening now.
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var addTrace = source.StartActivity("request-a")!;
        var addTraceId = addTrace.TraceId;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();

        using var drainTrace = source.StartActivity("request-b")!;
        await DispatchAsync(envelope);

        var span = Assert.Single(recorded);
        Assert.NotEqual(addTraceId, span.TraceId);
        Assert.Equal(drainTrace.TraceId, span.TraceId);
    }

    [Fact]
    public async Task Dispatch_span_from_a_timer_drain_roots_its_own_trace()
    {
        // The timer and reminder drain paths are not incoming grain calls, so no
        // activity is ambient. The span roots a fresh trace and the link is the
        // only route back to the request that appended the message.
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var addTrace = source.StartActivity("request-a")!;
        var addTraceId = addTrace.TraceId;
        var envelope = Envelope(1, addTrace.Id);
        addTrace.Dispose();

        await DispatchAsync(envelope);

        var span = Assert.Single(recorded);
        Assert.Null(span.Parent);
        Assert.NotEqual(addTraceId, span.TraceId);
        Assert.Equal(addTraceId, Assert.Single(span.Links).Context.TraceId);
    }

    [Fact]
    public async Task Concurrent_dispatch_links_each_message_to_its_own_trace()
    {
        // Items sharing a postman dispatch sequentially within one group, so this
        // registers a postman per item to force two groups and exercise the
        // Task.WhenAll path. Each postman blocks until both have started, so the
        // two deliveries genuinely overlap and any Activity.Current leakage between
        // groups would show up as a crossed link.
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        var first = source.StartActivity("request-a")!;
        var firstTraceId = first.TraceId;
        var firstEnvelope = Envelope(1, first.Id);
        first.Dispose();
        var second = source.StartActivity("request-b")!;
        var secondTraceId = second.TraceId;
        var secondEnvelope = Envelope(2, second.Id);
        second.Dispose();
        var firstArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentDictionary<long, ActivityTraceId>();

        await DispatchConcurrentlyAsync(
            async item =>
            {
                // Yield first. The dispatcher starts groups in a loop and only reaches
                // the second one once the first awaits, so a postman that blocks
                // synchronously would wait for a group that never starts.
                await Task.Yield();
                var (self, other) = item.Id.SequenceNumber == 1
                    ? (firstArrived, secondArrived.Task)
                    : (secondArrived, firstArrived.Task);
                self.SetResult();

                // Throws on timeout rather than returning a flag, so a regression that
                // serialises the groups fails the test instead of passing slowly.
                await other.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                observed[item.Id.SequenceNumber] = Activity.Current!.TraceId;
            },
            firstEnvelope,
            secondEnvelope);

        Assert.Equal(2, recorded.Count);
        Assert.Contains(recorded, span => span.Links.Single().Context.TraceId == firstTraceId);
        Assert.Contains(recorded, span => span.Links.Single().Context.TraceId == secondTraceId);
        Assert.NotEqual(observed[1], observed[2]);
    }

    [Fact]
    public async Task Failed_delivery_marks_the_dispatch_span_as_errored()
    {
        // A delivery that threw must not export as Unset, which reads as success.
        using var recorder = RecordDispatchSpans(out var recorded);

        await DispatchAsync(
            static _ => throw new InvalidOperationException("postman exploded"),
            Envelope(1, traceParent: null));

        var span = Assert.Single(recorded);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("postman exploded", span.StatusDescription);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem("exception.type"));
    }

    [Fact]
    public async Task Dispatch_without_a_captured_trace_context_starts_an_unlinked_span()
    {
        using var recorder = RecordDispatchSpans(out var recorded);

        await DispatchAsync(Envelope(1, traceParent: null));

        var span = Assert.Single(recorded);
        Assert.Empty(span.Links);
    }

    [Fact]
    public async Task Postman_runs_inside_the_per_item_dispatch_span()
    {
        // This is what lets EnrichedEventHubAdapter.ToQueueMessage keep stamping
        // Activity.Current?.Id unchanged and still be correct: the current
        // activity it observes is the per-item dispatch span, not whichever
        // request happened to trigger the drain.
        using var testListener = StartTestListener();
        using var recorder = RecordDispatchSpans(out var recorded);
        using var drainTrace = source.StartActivity("request-b")!;
        var observed = new List<Activity?>();

        await DispatchAsync(_ => observed.Add(Activity.Current), Envelope(1, traceParent: null));

        var span = Assert.Single(recorded);
        Assert.Same(span, Assert.Single(observed));
    }

    private static OutboxMessageEnvelope<string> Envelope(long sequenceNumber, string? traceParent) =>
        new(new OutboxMessageId(sequenceNumber, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, traceParent), "message");

    private Task DispatchAsync(params OutboxMessageEnvelope<string>[] pending) =>
        DispatchAsync(static _ => { }, pending);

    private Task DispatchAsync(
        Action<OutboxMessageEnvelope<string>> postman,
        params OutboxMessageEnvelope<string>[] pending)
    {
        var registry = new OutboxPostmanRegistry<OutboxMessageEnvelope<string>>();
        registry.Add(typeof(string), static _ => true, (item, _) =>
        {
            postman(item);
            return ValueTask.CompletedTask;
        });
        return DispatchAsync(registry, pending);
    }

    // The dispatcher groups items by the postman they match and runs groups under
    // Task.WhenAll, so one registration per item is what makes deliveries overlap.
    private Task DispatchConcurrentlyAsync(
        Func<OutboxMessageEnvelope<string>, ValueTask> postman,
        params OutboxMessageEnvelope<string>[] pending)
    {
        var registry = new OutboxPostmanRegistry<OutboxMessageEnvelope<string>>();
        foreach (var envelope in pending)
        {
            var sequenceNumber = envelope.Id.SequenceNumber;
            registry.Add(
                typeof(string),
                item => item.Id.SequenceNumber == sequenceNumber,
                (item, _) => postman(item));
        }

        return DispatchAsync(registry, pending);
    }

    private async Task DispatchAsync(
        OutboxPostmanRegistry<OutboxMessageEnvelope<string>> registry,
        OutboxMessageEnvelope<string>[] pending)
    {
        var dispatcher = new OutboxDispatcher<OutboxMessageEnvelope<string>>(
            registry,
            NullLogger.Instance,
            grainType,
            time,
            trace,
            static _ => typeof(string),
            static item => item.Id.TraceParent,
            static item => item.Id.Timestamp);

        await dispatcher.DispatchAsync(
            [.. pending],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);
    }

    private ActivityListener StartTestListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private ActivityListener RecordDispatchSpans(out ConcurrentBag<Activity> recorded)
    {
        // Concurrent groups stop their spans on different continuations, so
        // ActivityStopped can run these callbacks in parallel.
        var captured = new ConcurrentBag<Activity>();
        recorded = captured;
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == "egil.orleans.messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped =>
            {
                if (stopped.GetTagItem("grain.type") as string == grainType)
                {
                    captured.Add(stopped);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
