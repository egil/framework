using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.AzureStorage;

public interface IAzureAppendBlobEventStorageProvider
{
    IEventStorage<TEvent> Create<TEvent>(IGrainContext grainContext);

    void Participate(ISiloLifecycle observer);
}