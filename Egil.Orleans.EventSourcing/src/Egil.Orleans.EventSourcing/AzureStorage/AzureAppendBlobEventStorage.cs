using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing;

public sealed partial class AzureAppendBlobEventStorage<TEvent>(
    AppendBlobClient client,
    JsonSerializerOptions jsonSerializerOptions,
    ILogger<AzureAppendBlobEventStorage<TEvent>> logger) : IEventStorage<TEvent>
{
    private static readonly ActivitySource ActivitySource = new ActivitySource("Egil.Orleans.EventSourcing");
    private static readonly Meter Meter = new Meter("Egil.Orleans.EventSourcing");
    private static readonly Counter<long> AppendEventsCounter = Meter.CreateCounter<long>("egil-orleans-eventsourcing-events-append", description: "The number of events appended to the event stream.");
    private static readonly Counter<long> ReadEventsCounter = Meter.CreateCounter<long>("egil-orleans-eventsourcing-events-read", description: "The number of events read from the event stream.");
    private static readonly Histogram<double> AppendEventsDurationHistogram = Meter.CreateHistogram<double>("egil-orleans-eventsourcing-events-append-duration", description: "The duration of AppendEventsAsync in milliseconds");
    private static readonly Histogram<double> ReadEventsDurationHistogram = Meter.CreateHistogram<double>("egil-orleans-eventsourcing-events-read-duration", description: "The duration of ReadEventsAsync in milliseconds");
    private static readonly string EventTypeName = typeof(TEvent).Name;
    private static readonly KeyValuePair<string, object?> EventTypeTag = new KeyValuePair<string, object?>("EventType", EventTypeName);
    private const int BlockHeaderLength = 4;

    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new RecyclableMemoryStreamManager();
    private readonly ILogger<AzureAppendBlobEventStorage<TEvent>> logger = logger;
    private static readonly AppendBlobCreateOptions CreateOptions = new AppendBlobCreateOptions()
    {
        Conditions = new() { IfNoneMatch = ETag.All }
    };
    private readonly AppendBlobAppendBlockOptions appendOptions = new AppendBlobAppendBlockOptions()
    {
        Conditions = new AppendBlobRequestConditions { IfNoneMatch = ETag.All }
    };
    private bool blobExists;

    public ValueTask<int> AppendEventAsync(TEvent @event, CancellationToken cancellationToken = default)
        => AppendEventsAsync([@event], cancellationToken);

    public async ValueTask<int> AppendEventsAsync(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"{EventTypeName}.AppendEventsAsync", ActivityKind.Client);
        Stopwatch? stopwatch = AppendEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        await CheckBlobExists(cancellationToken);

        var appended = 0;

        try
        {
            foreach (var (stream, addedEvents) in AddEventsToStreams(events))
            {
                var result = await client.AppendBlockAsync(stream, appendOptions, cancellationToken);
                appendOptions.Conditions.IfNoneMatch = default;
                appendOptions.Conditions.IfMatch = result.Value.ETag;
                appended += addedEvents;
            }
        }
        catch (Exception ex)
        {
            LogFailedToApplyEvents(ex);
            throw;
        }

        AppendEventsCounter.Add(appended, EventTypeTag);

        if (stopwatch is not null)
        {
            stopwatch.Stop();
            AppendEventsDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, EventTypeTag);
        }

        return appended;
    }

    private IEnumerable<(RecyclableMemoryStream Stream, int EventsAdded)> AddEventsToStreams(IEnumerable<TEvent> events)
    {
        var maxStreamLength = client.AppendBlobMaxAppendBlockBytes;
        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(4);
        var stream = MemoryStreamManager.GetStream(nameof(AzureAppendBlobEventStorage<TEvent>.AppendEventsAsync));
        try
        {
            var addedToStream = 0;

            foreach (var @event in events)
            {
                RetryWithNewStream: // Label to jump back to when the event is too large to fit in a single block.

                var positionBeforeAdd = stream.Position;
                AddEventToStream(stream, headerBuffer, @event);

                // Check if the current event is too big to fit in a single block blob.
                if (stream.Length >= maxStreamLength)
                {
                    // Discard the current events bytes written to the stream.
                    stream.SetLength(positionBeforeAdd);

                    // The current event is too big to fit in a single block blob.
                    if (addedToStream == 0)
                    {
                        continue;
                    }

                    // There are events added to the stream that can be written to the blob.
                    if (stream.Length > 0)
                    {
                        stream.Position = 0;
                        yield return (stream, addedToStream);
                    }

                    // Reset stream and retry the current event
                    stream.SetLength(0);
                    stream.Position = 0;
                    addedToStream = 0;
                    goto RetryWithNewStream;
                }

                addedToStream++;
            }

            // When done with iterating over all events,
            // only return the stream if it actually have content inside it.
            if (stream.Length > 0)
            {
                stream.Position = 0;
                yield return (stream, addedToStream);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
            stream.Dispose();
        }

        void AddEventToStream(RecyclableMemoryStream stream, byte[] headerBuffer, TEvent @event)
        {
            // Write a dummy header (4 zero bytes) that will be updated below.
            long headerPosition = stream.Position;
            stream.Write(headerBuffer.AsSpan(0, BlockHeaderLength));

            // Serialize the event into the memory stream.
            JsonSerializer.Serialize(stream, @event, jsonSerializerOptions);

            // Calculate payload length (event size without header).
            int payloadLength = (int)(stream.Position - headerPosition - BlockHeaderLength);

            // Write the actual header using the rented buffer.
            BinaryPrimitives.WriteInt32LittleEndian(headerBuffer, payloadLength);

            // Overwrite header in the stream.
            long currentPosition = stream.Position;
            stream.Position = headerPosition;
            stream.Write(headerBuffer.AsSpan(0, BlockHeaderLength));
            stream.Position = currentPosition;
        }
    }

    public async IAsyncEnumerable<TEvent> ReadEventsAsync(int fromVersion = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"{EventTypeName}.ReadEventsAsync", ActivityKind.Client);
        Stopwatch? stopwatch = ReadEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        var stream = await TryGetBlockBlobStream(cancellationToken);
        if (stream is null)
        {
            blobExists = false;
            yield break;
        }

        int currentVersion = 0;
        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(4);
        var headerMem = headerBuffer.AsMemory(0, BlockHeaderLength);
        byte[] payloadBuffer = [];
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Read exactly 4 bytes for the length header.
                int bytesRead = await ReadAsync(stream, headerMem, cancellationToken);

                if (bytesRead == 0)
                {
                    // End of stream.
                    break;
                }

                if (bytesRead < BlockHeaderLength)
                {
                    LogIncompleteBlockHeader(currentVersion + 1, bytesRead);
                    break;
                }

                int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

                // Rent a buffer for the payload.
                payloadBuffer = RentOrReuse(payloadBuffer, payloadLength);
                bytesRead = await ReadAsync(stream, payloadBuffer.AsMemory(0, payloadLength), cancellationToken);

                if (bytesRead < payloadLength)
                {
                    LogIncompletePayload(currentVersion + 1, payloadLength, bytesRead);
                    break;
                }

                currentVersion++;

                if (currentVersion < fromVersion)
                {
                    continue;
                }

                var @event = TryDeserialize(payloadBuffer.AsSpan(0, payloadLength), currentVersion);
                if (@event is not null)
                {
                    yield return @event;
                }
            }
        }
        finally
        {
            ReturnBuffer(payloadBuffer);
            ReturnBuffer(headerBuffer);
            await stream.DisposeAsync();

            ReadEventsCounter.Add(currentVersion, EventTypeTag);

            if (stopwatch is not null)
            {
                stopwatch.Stop();
                ReadEventsDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, EventTypeTag);
            }

        }

        static byte[] RentOrReuse(byte[] buffer, int minLength)
        {
            if (buffer.Length < minLength)
            {
                ReturnBuffer(buffer);
                return ArrayPool<byte>.Shared.Rent(minLength);
            }

            return buffer;
        }

        static void ReturnBuffer(byte[] buffer)
        {
            if (buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private async Task<Stream?> TryGetBlockBlobStream(CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return result.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status is 404)
        {
            return null;
        }
    }

    private TEvent? TryDeserialize(ReadOnlySpan<byte> payload, int version)
    {
        try
        {
            var result = JsonSerializer.Deserialize<TEvent>(payload, jsonSerializerOptions);

            if (result is null)
            {
                LogDeserializationReturnedNull(version);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogErrorDeserializingEvent(ex, version);
            return default;
        }
    }

    private async Task CheckBlobExists(CancellationToken cancellationToken)
    {
        if (blobExists)
        {
            return;
        }

        try
        {
            var response = await client.CreateAsync(CreateOptions, cancellationToken);
            appendOptions.Conditions.IfNoneMatch = default;
            appendOptions.Conditions.IfMatch = response.Value.ETag;
            blobExists = true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            blobExists = true;
        }
    }

    private static async ValueTask<int> ReadAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to apply events to log.")]
    private partial void LogFailedToApplyEvents(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deserialization of event {Version} from event storage returned null.")]
    private partial void LogErrorDeserializingEvent(Exception exception, int version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize event {Version} from event storage.")]
    private partial void LogDeserializationReturnedNull(int version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read the expected payload bytes for event {Version}. Read {BytesRead} bytes of {ExpectedPayloadLength}.")]
    private partial void LogIncompletePayload(int version, int expectedPayloadLength, int bytesRead);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read the block header for event {Version}. Read {BytesRead} bytes of 4.")]
    private partial void LogIncompleteBlockHeader(int version, int bytesRead);
}