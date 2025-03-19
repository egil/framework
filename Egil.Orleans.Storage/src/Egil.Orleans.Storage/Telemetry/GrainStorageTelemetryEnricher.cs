using System.Diagnostics;
using System.Diagnostics.Metrics;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Egil.Orleans.Storage.Telemetry;

internal sealed class GrainStorageTelemetryEnricher(string storageName, IGrainStorage inner) : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    private const string StorageNameTagKey = "storage-name";
    private const string StateNameTagKey = "state-name";
    internal static readonly ActivitySource ActivitySource = new ActivitySource("Microsoft.Orleans.GrainStorage");
    private static readonly Meter Meter = new Meter("Microsoft.Orleans.GrainStorage");
    private static readonly Counter<long> ClearStateCounter = Meter.CreateCounter<long>("orleans-storage-clear", description: "The number of times the clear operation has been used.");
    private static readonly Counter<long> ReadStateCounter = Meter.CreateCounter<long>("orleans-storage-read", description: "The number of times the read operation has been used.");
    private static readonly Counter<long> WriteStateCounter = Meter.CreateCounter<long>("orleans-storage-write", description: "The number of times the write operation has been used.");
    private static readonly Histogram<double> ClearStateDurationHistogram = Meter.CreateHistogram<double>("orleans-storage-clear-duration", description: "The duration of clear operation in milliseconds.");
    private static readonly Histogram<double> ReadStateDurationHistogram = Meter.CreateHistogram<double>("orleans-storage-read-duration", description: "The duration of read operation in milliseconds.");
    private static readonly Histogram<double> WriteStateDurationHistogram = Meter.CreateHistogram<double>("orleans-storage-write-duration", description: "The duration of write operation in milliseconds.");
    private readonly KeyValuePair<string, object?> storageNameTag = new KeyValuePair<string, object?>(StorageNameTagKey, storageName);

    public void Participate(ISiloLifecycle lifecycle)
    {
        if (inner is ILifecycleParticipant<ISiloLifecycle> participant)
        {
            participant.Participate(lifecycle);
        }
    }

    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        using var activity = ActivitySource.StartActivity($"{storageName}.{stateName}.ClearStateAsync", ActivityKind.Client);
        activity?.AddTag(StorageNameTagKey, storageName);
        activity?.AddTag(StateNameTagKey, stateName);

        var stopwatch = ClearStateDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        await inner.ClearStateAsync(stateName, grainId, grainState);

        var stateNameTag = new KeyValuePair<string, object?>(StateNameTagKey, stateName);

        if(stopwatch is not null)
        {
            stopwatch.Stop();
            ClearStateDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, storageNameTag, stateNameTag);
        }

        ClearStateCounter.Add(1, storageNameTag, stateNameTag);
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        using var activity = ActivitySource.StartActivity($"{storageName}.{stateName}.ReadStateAsync", ActivityKind.Client);
        activity?.AddTag(StorageNameTagKey, storageName);
        activity?.AddTag(StateNameTagKey, stateName);

        var stopwatch = ReadStateDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        await inner.ReadStateAsync(stateName, grainId, grainState);

        var stateNameTag = new KeyValuePair<string, object?>(StateNameTagKey, stateName);

        if(stopwatch is not null)
        {
            stopwatch.Stop();
            ReadStateDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, storageNameTag, stateNameTag);
        }

        ReadStateCounter.Add(1, storageNameTag, stateNameTag);
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        using var activity = ActivitySource.StartActivity($"{storageName}.{stateName}.WriteStateAsync", ActivityKind.Client);
        activity?.AddTag(StorageNameTagKey, storageName);
        activity?.AddTag(StateNameTagKey, stateName);

        var stopwatch = WriteStateDurationHistogram.Enabled ? Stopwatch.StartNew() : null;

        await inner.WriteStateAsync(stateName, grainId, grainState);

        var stateNameTag = new KeyValuePair<string, object?>(StateNameTagKey, stateName);

        if(stopwatch is not null)
        {
            stopwatch.Stop();
            WriteStateDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, storageNameTag, stateNameTag);
        }

        WriteStateCounter.Add(1, storageNameTag, stateNameTag);
    }
}