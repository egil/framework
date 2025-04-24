using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Egil.Orleans.EventSourcing.AzureStorage;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing;

public sealed partial class AzureAppendBlobEventStorage<TEvent>(
    AppendBlobClient client,
    JsonSerializerOptions jsonSerializerOptions,
    ILogger<AzureAppendBlobEventStorage<TEvent>> logger) : IEventStorage<TEvent>
{
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
    private bool? blobExists;

    public ValueTask<int> AppendEventAsync(TEvent @event, CancellationToken cancellationToken = default)
        => AppendEventsAsync([@event], cancellationToken);

    public async ValueTask<int> AppendEventsAsync(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry<TEvent>.StartActivity(nameof(AppendEventsAsync));
        Stopwatch? stopwatch = Telemetry<TEvent>.AppendEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        await CreateIfNotExistsAsync(cancellationToken);

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
        finally
        {
            Telemetry<TEvent>.AppendEventsCounter.Add(appended, Telemetry<TEvent>.EventTypeTag);
            if (stopwatch is not null)
            {
                stopwatch.Stop();
                Telemetry<TEvent>.AppendEventsDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, Telemetry<TEvent>.EventTypeTag);
            }
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
        using var activity = Telemetry<TEvent>.StartActivity(nameof(ReadEventsAsync));
        Stopwatch? stopwatch = Telemetry<TEvent>.ReadEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        var stream = await TryGetBlockBlobStream(cancellationToken);
        if (stream is null)
        {
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

            Telemetry<TEvent>.ReadEventsCounter.Add(currentVersion, Telemetry<TEvent>.EventTypeTag);

            if (stopwatch is not null)
            {
                stopwatch.Stop();
                Telemetry<TEvent>.ReadEventsDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, Telemetry<TEvent>.EventTypeTag);
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
        if (!await CheckBlobExistsAsync(cancellationToken))
        {
            return null;
        }

        try
        {
            var result = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return result.Value.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
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

    private async ValueTask CreateIfNotExistsAsync(CancellationToken cancellationToken)
    {
        if (blobExists.HasValue && blobExists.Value)
        {
            return;
        }

        try
        {
            var response = await client.CreateIfNotExistsAsync(CreateOptions, cancellationToken);

            if (response?.Value is { } info)
            {
                appendOptions.Conditions.IfMatch = info.ETag;
                appendOptions.Conditions.IfNoneMatch = default;
            }

            blobExists = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Operation was canceled, rethrow the exception to propagate it.
            throw;
        }
        catch (Exception ex)
        {
            LogUnexpectedException(ex);
        }
    }

    private async ValueTask<bool> CheckBlobExistsAsync(CancellationToken cancellationToken)
    {
        if (blobExists.HasValue)
        {
            return blobExists.Value;
        }

        try
        {
            var response = await client.ExistsAsync(cancellationToken);
            blobExists = response.Value;
            return blobExists.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            LogUnexpectedException(ex);
            return false;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected exception occurred.")]
    private partial void LogUnexpectedException(Exception exception);

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