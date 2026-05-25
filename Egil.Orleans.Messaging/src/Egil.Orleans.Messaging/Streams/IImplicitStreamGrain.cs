using Orleans.Streams.Core;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Orleans grain marker interface that forwards implicit stream subscription
/// callbacks to the grain's attached <see cref="StreamManager"/>.
/// </summary>
public interface IImplicitStreamGrain : IStreamSubscriptionObserver
{
    Task IStreamSubscriptionObserver.OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var grainBase = (IGrainBase)this;
        var component = grainBase.GrainContext.GetComponent<IStreamManagerComponent>();
        return component is null
            ? Task.CompletedTask
            : component.OnSubscribedAsync(handleFactory);
    }
}
