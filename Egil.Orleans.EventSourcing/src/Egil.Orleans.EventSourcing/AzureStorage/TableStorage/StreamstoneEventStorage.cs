using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Streamstone;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EventStream = Streamstone.Stream;

namespace Egil.Orleans.EventSourcing.AzureStorage.TableStorage;

public sealed partial class StreamstoneEventStorage<TEvent>(
    Partition partition,
    JsonSerializerOptions jsonSerializerOptions,
    ILogger<StreamstoneEventStorage<TEvent>> logger) : IEventStorage<TEvent>
{
    private const string DataColumnName = "Data";
    private const string EventTypeColumnName = "EventType";
    private const string VersionColumnName = "Version";
    private EventStream? stream;

    public ValueTask<int> AppendEventAsync(TEvent @event, CancellationToken cancellationToken = default)
        => AppendEventsAsync([@event], cancellationToken);

    public async ValueTask<int> AppendEventsAsync(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry<TEvent>.StartActivity(nameof(AppendEventsAsync));
        Stopwatch? stopwatch = Telemetry<TEvent>.AppendEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;
        var appended = 0;

        try
        {
            stream ??= await GetEventStream();
            var result = await EventStream.WriteAsync(stream, [.. events.Select(ToEventData)]);
            stream = result.Stream;
            appended = result.Events.Length;
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

    public async IAsyncEnumerable<TEvent> ReadEventsAsync(int fromVersion = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry<TEvent>.StartActivity(nameof(ReadEventsAsync));
        Stopwatch? stopwatch = Telemetry<TEvent>.ReadEventsDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        var readEvents = 0;

        try
        {
            stream ??= await GetEventStream();

            var nextSliceStart = fromVersion == 0 ? 1 : fromVersion;
            if (stream.Version + 1 < nextSliceStart)
            {
                yield break;
            }

            StreamSlice<TableEntity> slice;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                slice = await EventStream.ReadAsync<TableEntity>(stream.Partition, nextSliceStart);
                foreach (var props in slice.Events)
                {
                    var result = TryDeserialize(props);

                    if (result.Event is not null)
                    {
                        readEvents++;
                        yield return result.Event;
                    }

                    nextSliceStart = result.Version + 1;
                }
            }
            while (!slice.IsEndOfStream);
        }
        finally
        {
            Telemetry<TEvent>.ReadEventsCounter.Add(readEvents, Telemetry<TEvent>.EventTypeTag);

            if (stopwatch is not null)
            {
                stopwatch.Stop();
                Telemetry<TEvent>.ReadEventsDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, Telemetry<TEvent>.EventTypeTag);
            }
        }
    }

    private async ValueTask<EventStream> GetEventStream()
    {
        if (stream is null)
        {
            var result = await EventStream.TryOpenAsync(partition);
            return result.Found ? result.Stream : await EventStream.ProvisionAsync(partition);
        }

        return stream;
    }

    private EventData ToEventData(TEvent @event)
    {
        var properties = new Dictionary<string, object?>
        {
            [EventTypeColumnName] = Telemetry<TEvent>.EventTypeName,
            [DataColumnName] = JsonSerializer.Serialize(@event, jsonSerializerOptions)
        };

        return new EventData(Streamstone.EventId.None, EventProperties.From(properties));
    }

    private (int Version, TEvent? Event) TryDeserialize(TableEntity tableEntity)
    {
        if (!tableEntity.TryGetValue(VersionColumnName, out var versionObj) || versionObj is not int version)
        {
            throw new InvalidOperationException("Version not found in event properties.");
        }

        if (!tableEntity.TryGetValue(DataColumnName, out var jsonObj) || jsonObj is not string json)
        {
            LogDeserializationFailedMissingData(version);
            return (version, default);
        }

        try
        {
            var result = JsonSerializer.Deserialize<TEvent>(json, jsonSerializerOptions);

            if (result is not null)
            {
                return (version, result);
            }

            LogDeserializationReturnedNull(version);
            return (version, default);
        }
        catch (Exception ex)
        {
            LogErrorDeserializingEvent(ex, version);
            return (version, default);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to apply events to log.")]
    private partial void LogFailedToApplyEvents(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize event {Version} from event storage.")]
    private partial void LogDeserializationReturnedNull(int version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize event {Version} from event storage. Event data not found in table row.")]
    private partial void LogDeserializationFailedMissingData(int version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deserialization of event {Version} from event storage returned null.")]
    private partial void LogErrorDeserializingEvent(Exception exception, int version);
}
