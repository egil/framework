using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.AzureStorage;

internal sealed class AzureAppendBlobEventStorageProvider(
    IOptions<AzureAppendBlobEventStorageOptions> options,
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory) : ILifecycleParticipant<ISiloLifecycle>, IAzureAppendBlobEventStorageProvider
{
    private readonly IBlobContainerFactory containerFactory = options.Value.BuildContainerFactory(serviceProvider, options.Value);
    private readonly AzureAppendBlobEventStorageOptions options = options.Value;

    private async Task Initialize(CancellationToken cancellationToken)
    {
        var client = await options.CreateClient(cancellationToken);

        await containerFactory
            .InitializeAsync(client, cancellationToken)
            .ConfigureAwait(false);
    }

    public IEventStorage<TEvent> Create<TEvent>(IGrainContext grainContext)
    {
        var container = containerFactory.GetBlobContainerClient(grainContext.GrainId);
        var blobName = options.GetBlobName(grainContext.GrainId);
        var blobClient = container.GetAppendBlobClient(blobName);
        return new AzureAppendBlobEventStorage<TEvent>(
            blobClient,
            options.JsonSerializerOptions,
            loggerFactory.CreateLogger<AzureAppendBlobEventStorage<TEvent>>());
    }

    public void Participate(ISiloLifecycle observer)
    {
        observer.Subscribe(
            nameof(AzureAppendBlobEventStorageProvider),
            ServiceLifecycleStage.RuntimeInitialize,
            onStart: Initialize);
    }
}