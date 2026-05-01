using Orleans.Storage;

namespace Egil.Orleans.Testing;

internal sealed class StorageObserver(
    IGrainStorage inner,
    GrainActivityCollector collector,
    string storageName)
    : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
        ObserveAsync(
            StorageOperationKind.Clear,
            stateName,
            grainId,
            grainState,
            () => inner.ClearStateAsync(stateName, grainId, grainState));

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
        ObserveAsync(
            StorageOperationKind.Read,
            stateName,
            grainId,
            grainState,
            () => inner.ReadStateAsync(stateName, grainId, grainState));

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
        ObserveAsync(
            StorageOperationKind.Write,
            stateName,
            grainId,
            grainState,
            () => inner.WriteStateAsync(stateName, grainId, grainState));

    public void Participate(ISiloLifecycle lifecycle)
    {
        if (inner is ILifecycleParticipant<ISiloLifecycle> participant)
        {
            participant.Participate(lifecycle);
        }
    }

    private async Task ObserveAsync<T>(
        StorageOperationKind kind,
        string stateName,
        GrainId grainId,
        IGrainState<T> grainState,
        Func<Task> operation)
    {
        await operation().ConfigureAwait(false);

        if (RequestContext.Get(RequestContextScope.AssertionKey) is true)
        {
            return;
        }

        collector.OnStorageOperation(new StorageOperation(
            kind,
            grainId,
            storageName,
            stateName,
            grainState.ETag,
            grainState.State));
    }
}
