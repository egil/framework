using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxDispatcherTraceContextTests : IDisposable
{
    private readonly ActivitySource source = new("test." + Guid.NewGuid().ToString("N"));

    // Test classes run in parallel and share the egil.orleans.messaging source,
    // so recorded spans are filtered by a grain type unique to this instance.
    private readonly string grainType = "TestGrain-" + Guid.NewGuid().ToString("N");

    public void Dispose() => source.Dispose();

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

        await DispatchAsync(firstEnvelope, secondEnvelope);

        Assert.Equal(2, recorded.Count);
        Assert.Contains(recorded, span => span.Links.Single().Context.TraceId == firstTraceId);
        Assert.Contains(recorded, span => span.Links.Single().Context.TraceId == secondTraceId);
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

    private async Task DispatchAsync(
        Action<OutboxMessageEnvelope<string>> postman,
        params OutboxMessageEnvelope<string>[] pending)
    {
        var registry = new OutboxPostmanRegistry<OutboxMessageEnvelope<string>>();
        registry.Add(typeof(string), static _ => true, (item, _) =>
        {
            postman(item);
            return ValueTask.CompletedTask;
        });
        var dispatcher = new OutboxDispatcher<OutboxMessageEnvelope<string>>(
            registry,
            NullLogger.Instance,
            grainType,
            TimeProvider.System,
            static _ => typeof(string),
            static item => item.Id.TraceParent);

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

    private ActivityListener RecordDispatchSpans(out List<Activity> recorded)
    {
        var captured = new List<Activity>();
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
