using System.Diagnostics;
using System.Diagnostics.Metrics;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Egil.Orleans.Storage.Telemetry;

internal sealed class GrainStorageTelemetryEnricher(string storageName, IGrainStorage inner) : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    internal static readonly ActivitySource ActivitySource = new ActivitySource("Microsoft.Orleans.GrainStorage");
    private static readonly Meter Meter = new Meter("Microsoft.Orleans.GrainStorage");
    private static readonly Counter<long> ClearStateCounter = Meter.CreateCounter<long>("orleans-storage-clear", description: "The number of times the clear operation has been used.");
    private static readonly Counter<long> ReadStateCounter = Meter.CreateCounter<long>("orleans-storage-read", description: "The number of times the read operation has been used.");
    private static readonly Counter<long> WriteStateCounter = Meter.CreateCounter<long>("orleans-storage-write", description: "The number of times the write operation has been used.");
    private readonly KeyValuePair<string, object?> storageNameTag = new KeyValuePair<string, object?>("storage-name", storageName);

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
        activity?.AddTag("stateName", stateName);
        activity?.AddTag("grainId", grainId.ToString());

        await inner.ClearStateAsync(stateName, grainId, grainState);

        ClearStateCounter.Add(1, storageNameTag, new KeyValuePair<string, object?>("state-name", stateName));
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        using var activity = ActivitySource.StartActivity($"{storageName}.{stateName}.ReadStateAsync", ActivityKind.Client);
        activity?.AddTag("stateName", stateName);
        activity?.AddTag("grainId", grainId.ToString());

        await inner.ReadStateAsync(stateName, grainId, grainState);

        ReadStateCounter.Add(1, storageNameTag, new KeyValuePair<string, object?>("state-name", stateName));
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        using var activity = ActivitySource.StartActivity($"{storageName}.{stateName}.WriteStateAsync", ActivityKind.Client);
        activity?.AddTag("stateName", stateName);
        activity?.AddTag("grainId", grainId.ToString());

        await inner.WriteStateAsync(stateName, grainId, grainState);

        WriteStateCounter.Add(1, storageNameTag, new KeyValuePair<string, object?>("state-name", stateName));
    }
}