using System.Collections.Concurrent;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Fakes;

public sealed class FakePublishingStreamProvider : IStreamProvider
{
    public const string ProviderName = "fanout-publications";
    private readonly ConcurrentDictionary<StreamId, ConcurrentQueue<Publication>> publications = new();
    private readonly ConcurrentDictionary<StreamId, byte> failures = new();

    public string Name => ProviderName;
    public bool IsRewindable => false;

    public Publication[] Read(StreamId streamId) => publications.TryGetValue(streamId, out var items) ? items.ToArray() : [];

    public void FailNext(StreamId streamId) => failures[streamId] = 0;

    public IAsyncStream<T> GetStream<T>(StreamId streamId) => new PublishingStream<T>(this, streamId);

    private async Task PublishAsync<T>(StreamId streamId, T message)
    {
        // Suspend so a postman that starts publications without awaiting them cannot
        // acknowledge successfully before an injected provider failure is observed.
        await Task.Yield();
        if (failures.TryRemove(streamId, out _))
        {
            throw new IOException("Injected publication failure.");
        }

        publications.GetOrAdd(streamId, static _ => new()).Enqueue(new(message!, RequestContext.GetOutboxToken()));
    }

    public sealed record Publication(object Message, OutboxSequenceToken? Token);

    private sealed class PublishingStream<T>(FakePublishingStreamProvider provider, StreamId streamId) : IAsyncStream<T>
    {
        public string ProviderName => provider.Name;
        public StreamId StreamId => streamId;
        public bool IsRewindable => false;
        public Task OnNextAsync(T item, StreamSequenceToken? token = null) => provider.PublishAsync(streamId, item);
        public Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token = null) => throw new NotSupportedException();
        public Task OnCompletedAsync() => throw new NotSupportedException();
        public Task OnErrorAsync(Exception ex) => throw new NotSupportedException();
        public Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles() => throw new NotSupportedException();
        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer) => throw new NotSupportedException();
        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token, string? filterData = null) => throw new NotSupportedException();
        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer) => throw new NotSupportedException();
        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token) => throw new NotSupportedException();
        public int CompareTo(IAsyncStream<T>? other) => string.Compare(StreamId.ToString(), other?.StreamId.ToString(), StringComparison.Ordinal);
        public bool Equals(IAsyncStream<T>? other) => other is not null && ProviderName == other.ProviderName && StreamId.Equals(other.StreamId);
    }
}
