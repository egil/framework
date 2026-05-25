using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;

namespace Egil.Orleans.Messaging;

internal static class MessagingTelemetry
{
    private static readonly Meter Meter = new("egil.orleans.messaging");
    private static readonly Counter<long> StreamMessages = Meter.CreateCounter<long>("stream.messages");
    private static readonly Counter<long> StreamSubscriptions = Meter.CreateCounter<long>("stream.subscriptions");
    private static readonly Counter<long> StreamHandlerErrors = Meter.CreateCounter<long>("stream.handler.errors");
    private static readonly Histogram<double> StreamHandlerDuration = Meter.CreateHistogram<double>("stream.handler.duration", "ms");
    private static readonly Counter<long> OutboxPostItems = Meter.CreateCounter<long>("outbox.post.items");
    private static readonly Counter<long> OutboxPostErrors = Meter.CreateCounter<long>("outbox.post.errors");
    private static readonly Histogram<double> OutboxPostDuration = Meter.CreateHistogram<double>("outbox.post.duration", "ms");
    private static readonly Histogram<double> OutboxPostItemDuration = Meter.CreateHistogram<double>("outbox.post.item.duration", "ms");
    private static readonly ConcurrentDictionary<string, long> OutboxDepthValues = [];
    private static readonly ObservableGauge<long> OutboxDepth = Meter.CreateObservableGauge(
        "outbox.depth",
        ObserveOutboxDepth,
        description: "Pending outbox items by grain type.");
    private static readonly Histogram<double> StreamReceiveLag = Meter.CreateHistogram<double>(
        "stream.message.receive.lag",
        "ms",
        "Elapsed time between an enriched stream message enqueue time and receiver-side acceptance.");
    private static readonly Histogram<double> OutboxReceiveLag = Meter.CreateHistogram<double>(
        "outbox.message.receive.lag",
        "ms",
        "Elapsed time between an outbox message timestamp and receiver-side acceptance.");

    public static readonly ActivitySource ActivitySource = new("egil.orleans.messaging");

    public static void RecordStreamMessage(string streamNamespace, string status) =>
        StreamMessages.Add(1, StreamTags(streamNamespace, status));

    public static void RecordStreamSubscription(string streamNamespace, string status) =>
        StreamSubscriptions.Add(1, StreamTags(streamNamespace, status));

    public static void RecordStreamHandlerError(string streamNamespace) =>
        StreamHandlerErrors.Add(1, StreamTags(streamNamespace, "handler"));

    public static void RecordStreamHandlerDuration(string streamNamespace, string status, double milliseconds) =>
        StreamHandlerDuration.Record(milliseconds, StreamTags(streamNamespace, status));

    public static void RecordOutboxPostDuration(
        string grainType,
        OutboxPostmanExecutionMode executionMode,
        double milliseconds)
    {
        OutboxPostDuration.Record(
            milliseconds,
            OutboxTags(grainType, executionMode));
    }

    public static void RecordOutboxPostItem(
        string grainType,
        string postmanType,
        OutboxPostmanExecutionMode executionMode,
        bool success,
        double milliseconds)
    {
        var tags = OutboxTags(grainType, executionMode);
        tags.Add("postman.type", postmanType);
        tags.Add("success", success);

        if (success)
        {
            OutboxPostItems.Add(1, tags);
        }

        OutboxPostItemDuration.Record(milliseconds, tags);
    }

    public static void RecordOutboxPostError(string grainType, string eventType, Exception exception)
    {
        OutboxPostErrors.Add(
            1,
            new TagList
            {
                { "grain.type", grainType },
                { "event.type", eventType },
                { "failure.type", exception.GetType().Name }
            });
    }

    public static void RecordOutboxDepth(string grainType, int depth)
    {
        _ = OutboxDepth;
        OutboxDepthValues[grainType] = depth;
    }

    public static void RecordOutboxReceiveLag(OutboxSequenceToken token, DateTimeOffset receivedAt)
    {
        var lag = ClampLag(receivedAt - token.Timestamp);
        OutboxReceiveLag.Record(
            lag.TotalMilliseconds,
            new TagList
            {
                { "messaging.system", "orleans" },
                { "messaging.operation", "receive" },
                { "messaging.source.kind", "outbox" },
                { "status", "accepted" },
                { "sender.grain.type", token.Sender.Type.ToString() }
            });
    }

    public static void RecordStreamReceiveLag(StreamCursor cursor, DateTimeOffset receivedAt)
    {
        if (!cursor.TryGetEnqueuedTime(out var enqueuedTime))
        {
            return;
        }

        var lag = ClampLag(receivedAt - enqueuedTime);
        var tags = new TagList
        {
            { "messaging.system", "orleans" },
            { "messaging.operation", "receive" },
            { "messaging.source.kind", "stream" },
            { "stream.namespace", cursor.StreamNamespace },
            { "status", "accepted" }
        };

        if (cursor.TryGetProviderName(out var providerName))
        {
            tags.Add("stream.provider", providerName);
        }

        StreamReceiveLag.Record(lag.TotalMilliseconds, tags);
    }

    private static TimeSpan ClampLag(TimeSpan lag)
    {
        return lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
    }

    private static TagList StreamTags(string streamNamespace, string status)
    {
        var tags = new TagList
        {
            { "stream.namespace", streamNamespace },
            { "status", status }
        };

        return tags;
    }

    private static TagList OutboxTags(string grainType, OutboxPostmanExecutionMode executionMode)
    {
        return new TagList
        {
            { "grain.type", grainType },
            { "postman.execution", executionMode is OutboxPostmanExecutionMode.ThreadPool ? "thread_pool" : "grain_scheduler" }
        };
    }

    private static IEnumerable<Measurement<long>> ObserveOutboxDepth()
    {
        foreach (var depth in OutboxDepthValues)
        {
            yield return new Measurement<long>(
                depth.Value,
                new KeyValuePair<string, object?>("grain.type", depth.Key));
        }
    }
}
