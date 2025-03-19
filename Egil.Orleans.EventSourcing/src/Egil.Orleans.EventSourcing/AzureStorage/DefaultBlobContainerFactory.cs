using Azure.Storage.Blobs;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.AzureStorage;

/// <summary>
/// A default blob container factory that uses the default container name.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DefaultBlobContainerFactory"/> class.
/// </remarks>
/// <param name="options">The blob storage options</param>
internal class DefaultBlobContainerFactory(AzureAppendBlobEventStorageOptions options) : IBlobContainerFactory
{
    private BlobContainerClient defaultContainer = null!;

    /// <inheritdoc/>
    public BlobContainerClient GetBlobContainerClient(GrainId grainId)
        => defaultContainer;

    /// <inheritdoc/>
    public async Task InitializeAsync(BlobServiceClient client, CancellationToken cancellationToken)
    {
        defaultContainer = client.GetBlobContainerClient(options.ContainerName);
        await defaultContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}
