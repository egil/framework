using System.Runtime.CompilerServices;
using Egil.Orleans.Testing;
using Orleans.Streams;
using Orleans.TestingHost;

namespace Egil.Orleans.Messaging.Tests;

public sealed class MessagingTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public InProcessTestCluster Cluster => cluster ?? throw new InvalidOperationException("Test cluster not initialized.");

    public IGrainFactory GrainFactory => Cluster.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");

            AddStreamProviders(siloBuilder);

            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFrom("Default");
        });

        builder.ConfigureClient(AddStreamProviders);

        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    public TGrain GetUniqueGrain<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => (TGrain)GrainFactory.GetGrain(typeof(TGrain), Guid.NewGuid());

    public IAsyncStream<T> GetStream<T>(string streamNamespace, Guid key)
    {
        var provider = Cluster.Client.GetStreamProvider(streamNamespace);
        return provider.GetStream<T>(StreamId.Create(streamNamespace, key));
    }

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);

    private static void AddStreamProviders(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryStreams(StreamManagerTestNamespaces.ValueTask);
        siloBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Task);
        siloBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Failure);
        siloBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Resume);
    }

    private static void AddStreamProviders(IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams(StreamManagerTestNamespaces.ValueTask);
        clientBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Task);
        clientBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Failure);
        clientBuilder.AddMemoryStreams(StreamManagerTestNamespaces.Resume);
    }
}
