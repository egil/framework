using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    /// <summary>Publishes each matching payload to a fixed collection of streams.</summary>
    /// <remarks>
    /// The supplied sequence is retained without copying and enumerated once per delivery
    /// attempt. Repeated IDs cause repeated publications. Supply a repeatable sequence;
    /// changes to its contents affect subsequent deliveries.
    /// An empty collection completes successfully without publishing.
    /// Publications are awaited sequentially in destination order. A failure stops
    /// delivery and leaves the item pending; retries start from the first destination
    /// with the same outbox token, so destinations may receive the message again.
    /// </remarks>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        IEnumerable<StreamId> streamIds)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(streamIds);
        return AddStreamFanOutPostman<TSub>(streamProviderName, (_, _) => streamIds);
    }

    /// <summary>Publishes each matching payload to the streams selected for that message.</summary>
    /// <remarks>
    /// The selector runs once per delivery attempt and its result is enumerated while
    /// publishing. Repeated IDs cause repeated publications; an empty result completes
    /// successfully without publishing. A null result fails delivery.
    /// Publications are awaited sequentially. A publication or enumeration failure stops
    /// delivery; retries repeat selection and publication with the same outbox token. Keep routing stable
    /// across retries when every originally selected destination must receive the item.
    /// </remarks>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, IEnumerable<StreamId>> streamIds)
        where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(streamIds);
        return AddStreamPostman<TSub>(streamProviderName, (message, _) => streamIds(message));
    }

    /// <summary>Publishes each payload to streams selected using the message and its delivery token.</summary>
    /// <remarks>
    /// The selector runs once per delivery attempt, outside the publication's request-context
    /// scope. Its result is enumerated while publishing, including repeated IDs.
    /// An empty result completes successfully without publishing; a null result fails delivery.
    /// Publications are awaited sequentially in destination order. A publication or enumeration failure
    /// stops delivery; retries repeat selection and publication using the same outbox token at every destination.
    /// Keep routing stable across retries when every originally selected destination must receive the item.
    /// </remarks>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, IEnumerable<StreamId>> streamIds)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(streamIds);
        return AddStreamFanOutPostman(streamProviderName, streamIds);
    }

    private OutboxProcessor<TOutbox> AddStreamFanOutPostman<TSub>(
        string streamProviderName,
        Func<TSub, OutboxSequenceToken, IEnumerable<StreamId>> selectDestinations)
        where TSub : TOutbox
    {
        var streamProvider = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(streamProviderName);
        return AddPostman<TSub>(async ValueTask (message, token, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Enumerate directly: order and repeated IDs belong to the caller's routing
            // policy. A later iterator failure is a partial delivery, just like a send failure.
            var destinations = selectDestinations(message, token)
                ?? throw new InvalidOperationException("The stream selector returned null.");
            foreach (var streamId in destinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await streamProvider.GetStream<TSub>(streamId).PublishFromOutboxAsync(message, token);
            }
        });
    }
}
