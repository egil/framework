using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

public interface IEventStorageProvider
{
    IEventStorage<TEvent> Create<TEvent>(IGrainContext grainContext);
}