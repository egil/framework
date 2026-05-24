using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Egil.Orleans.Messaging;

internal static class MessagingTelemetry
{
    private static readonly Meter Meter = new("egil.orleans.messaging");
    private static readonly Counter<long> StreamMessages = Meter.CreateCounter<long>("stream.messages");
    private static readonly Counter<long> StreamSubscriptions = Meter.CreateCounter<long>("stream.subscriptions");
    private static readonly Counter<long> StreamHandlerErrors = Meter.CreateCounter<long>("stream.handler.errors");
    private static readonly Histogram<double> StreamHandlerDuration = Meter.CreateHistogram<double>("stream.handler.duration", "ms");
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
            { "stream.namespace", cursor.StreamId.GetNamespace() },
            { "status", "accepted" }
        };

        if (cursor.TryGetStreamProviderName(out var streamProviderName))
        {
            tags.Add("stream.provider", streamProviderName);
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
}
