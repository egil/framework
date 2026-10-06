using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Tracking;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Journaling;

internal sealed class DurableMessageTracker : JournaledSnapshot<MessageTracker, TrackerOperation>, IDurableMessageTracker
{
    private readonly TimeProvider time;
    private TimeProvider? ownTime;
    private MessageTrackerSettings? configuration;

    public DurableMessageTracker(TimeProvider time, JournalCodec<TrackerOperation> codec)
        : base(new MessageTracker(), codec.Value)
    {
        this.time = time;
    }

    public void RegisterTimeProvider(TimeProvider time) => ownTime = time;

    public void Configure(Action<MessageTrackerOptions> configure)
    {
        configuration = (configuration ?? MessageTrackerDefaults.Current).Configure(configure);
        Current.Configuration = configuration;
    }

    public StreamCursor? LatestStream(string streamNamespace) => Current.LatestStream(streamNamespace);
    public StreamSequenceToken? LatestStreamSequenceToken(string streamNamespace) => Current.LatestStreamSequenceToken(streamNamespace);
    public StreamCursor? LatestStream(string streamProviderName, string streamNamespace) => Current.LatestStream(streamProviderName, streamNamespace);
    public StreamSequenceToken? LatestStreamSequenceToken(string streamProviderName, string streamNamespace) => Current.LatestStreamSequenceToken(streamProviderName, streamNamespace);
    public StreamCursor? LatestStream(StreamId stream) => Current.LatestStream(stream);
    public StreamCursor? LatestStream(string streamProviderName, StreamId stream) => Current.LatestStream(streamProviderName, stream);
    public StreamSequenceToken? LatestStreamSequenceToken(string streamProviderName, StreamId stream) => Current.LatestStreamSequenceToken(streamProviderName, stream);
    public OutboxSequenceToken? LatestOutbox(GrainId sender) => Current.LatestOutbox(sender);

    public bool TryAcceptMessage(string streamNamespace, StreamSequenceToken? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        return TryAcceptMessage(new StreamCursor(streamNamespace, token));
    }

    public bool TryAcceptMessage(string streamProviderName, string streamNamespace, StreamSequenceToken? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        return TryAcceptMessage(new StreamCursor(streamNamespace, token, streamProviderName));
    }

    public bool TryAcceptMessage(OutboxSequenceToken token, out IDurableMessageTracker next)
    {
        var accepted = TryAcceptMessage(token);
        next = this;
        return accepted;
    }

    public bool TryAcceptMessage(StreamCursor cursor, out IDurableMessageTracker next)
    {
        var accepted = TryAcceptMessage(cursor);
        next = this;
        return accepted;
    }

    public bool TryAcceptMessage(string streamNamespace, StreamSequenceToken? token, out IDurableMessageTracker next)
    {
        var accepted = TryAcceptMessage(streamNamespace, token);
        next = this;
        return accepted;
    }

    public bool TryAcceptMessage(string streamProviderName, string streamNamespace, StreamSequenceToken? token, out IDurableMessageTracker next)
    {
        var accepted = TryAcceptMessage(streamProviderName, streamNamespace, token);
        next = this;
        return accepted;
    }

    public bool TryAcceptMessage(OutboxSequenceToken token)
    {
        var settings = ConfigureCurrent();
        if (!Current.TryAcceptMessage(token, out var next, out var acceptance, settings))
        {
            return false;
        }

        Stage(next, new TrackerOperation("outbox", OutboxToken: token, Received: acceptance.Received, RetentionCutoff: acceptance.RetentionCutoff));
        return true;
    }

    public bool TryAcceptMessage(StreamCursor cursor)
    {
        var settings = ConfigureCurrent();
        if (!Current.TryAcceptMessage(cursor, out var next, out var acceptance, settings))
        {
            return false;
        }

        // Even a tokenless delivery can remove expired entries. Record its resolved policy
        // in the same operation so recovery reproduces cleanup without consulting the clock.
        if (!ReferenceEquals(next, Current))
        {
            Stage(next, new TrackerOperation("stream", Stream: acceptance.Stream, Received: acceptance.Received, RetentionCutoff: acceptance.RetentionCutoff));
        }

        return true;
    }

    public IDurableMessageTracker Evict(DateTimeOffset olderThan) =>
        StageEviction(Current.Evict(olderThan), new TrackerOperation("evict", Received: olderThan));

    public IDurableMessageTracker EvictStreams(DateTimeOffset olderThan) =>
        StageEviction(Current.EvictStreams(olderThan), new TrackerOperation("evict-streams", Received: olderThan));

    public IDurableMessageTracker EvictStreamReceipts(DateTimeOffset olderThan) =>
        StageEviction(Current.EvictStreamReceipts(olderThan), new TrackerOperation("evict-receipts", Received: olderThan));

    public IDurableMessageTracker EvictOutboxes(DateTimeOffset olderThan) =>
        StageEviction(Current.EvictOutboxes(olderThan), new TrackerOperation("evict-outboxes", Received: olderThan));

    public IDurableMessageTracker Evict(string streamNamespace, DateTimeOffset olderThan) =>
        StageEviction(Current.Evict(streamNamespace, olderThan),
            new TrackerOperation("evict-namespace", Received: olderThan, StreamNamespace: streamNamespace));

    public IDurableMessageTracker Evict(StreamId stream, DateTimeOffset olderThan) =>
        StageEviction(Current.Evict(stream, olderThan), new TrackerOperation("evict-stream", Received: olderThan, StreamId: StreamIdJsonModel.From(stream)));

    public IDurableMessageTracker Evict(string streamProviderName, StreamId stream, DateTimeOffset olderThan) =>
        StageEviction(Current.Evict(streamProviderName, stream, olderThan), new TrackerOperation("evict-stream", Received: olderThan, StreamId: StreamIdJsonModel.From(stream), ProviderName: streamProviderName));

    public IDurableMessageTracker Evict(GrainId sender, DateTimeOffset olderThan) =>
        StageEviction(Current.Evict(sender, olderThan), new TrackerOperation("evict-sender", Received: olderThan, Sender: sender));

    private IDurableMessageTracker StageEviction(MessageTracker next, TrackerOperation operation)
    {
        if (!ReferenceEquals(next, Current))
        {
            Stage(next, operation);
        }

        return this;
    }

    protected override MessageTracker Empty() => new();
    protected override TrackerOperation Snapshot(MessageTracker value) => new("snapshot", State: value);
    private MessageTrackerSettings ConfigureCurrent()
    {
        var settings = configuration ?? MessageTrackerDefaults.Current;
        Current.Configuration = configuration;
        Current.RegisterTimeProvider(ownTime ?? settings.TimeProvider ?? time);
        return settings;
    }

    protected override MessageTracker Apply(MessageTracker value, TrackerOperation operation)
    {
        // Replay applies the recorded facts directly: no fresh clock, dedup decision, or receive telemetry.
        if (operation.RetentionCutoff is { } cutoff)
            value = value.Evict(cutoff);
        switch (operation.Kind)
        {
            case "snapshot":
                return operation.State ?? throw new InvalidOperationException("Missing tracker snapshot.");
            case "outbox" when operation.OutboxToken is { } token:
                return new MessageTracker(value.StreamEntries, value.OutboxEntries.SetItem(token.Sender,
                    new MessageTracker.OutboxEntry(token.Epoch, token.SequenceNumber, operation.Received, token.Timestamp)), value.StreamReceipts);
            case "stream" when operation.Stream is { } cursor:
                return value.RecordStreamAcceptance(cursor, operation.Received);
            case "evict":
                return value.Evict(operation.Received);
            case "evict-streams":
                return value.EvictStreams(operation.Received);
            case "evict-receipts":
                return value.EvictStreamReceipts(operation.Received);
            case "evict-outboxes":
                return value.EvictOutboxes(operation.Received);
            case "evict-namespace" when operation.StreamNamespace is { } streamNamespace:
                return value.Evict(streamNamespace, operation.Received);
            case "evict-stream" when operation.StreamId is { } streamId:
                return operation.ProviderName is { } providerName
                    ? value.Evict(providerName, streamId.ToStreamId(), operation.Received)
                    : value.Evict(streamId.ToStreamId(), operation.Received);
            case "evict-sender" when operation.Sender is { } sender:
                return value.Evict(sender, operation.Received);
            default:
                throw new InvalidOperationException($"Unknown tracker operation '{operation.Kind}'.");
        }
    }
}

internal sealed record TrackerOperation(
    string Kind,
    OutboxSequenceToken? OutboxToken = null,
    StreamCursor? Stream = null,
    DateTimeOffset Received = default,
    MessageTracker? State = null,
    string? StreamNamespace = null,
    GrainId? Sender = null,
    StreamIdJsonModel? StreamId = null,
    string? ProviderName = null,
    DateTimeOffset? RetentionCutoff = null);
