using Orleans.Streams.Core;

namespace Egil.Orleans.Messaging.Streams;

internal interface IStreamManagerComponent
{
    Task OnSubscribedAsync(IStreamSubscriptionHandleFactory handleFactory);
}