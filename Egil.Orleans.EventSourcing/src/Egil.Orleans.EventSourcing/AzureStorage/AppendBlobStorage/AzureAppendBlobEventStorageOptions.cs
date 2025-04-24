using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Orleans;
using Orleans.Runtime;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.AzureStorage.AppendBlobStorage;

public class AzureAppendBlobEventStorageOptions
{
    private static readonly Func<GrainId, string> DefaultGetBlobName = static (grainId) => $"{grainId}.bin";

    public const string DefaultContainerName = "events";
    public const int DefaultInitStage = ServiceLifecycleStage.ApplicationServices;

    /// <summary>
    /// Container name where state machine state is stored.
    /// </summary>
    public string ContainerName { get; set; } = DefaultContainerName;

    public Func<GrainId, string> GetBlobName { get; set; } = DefaultGetBlobName;

    /// <summary>
    /// Options to be used when configuring the blob storage client, or <see langword="null"/> to use the default options.
    /// </summary>
    public BlobClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// Configures options for JSON serialization, using default settings optimized for web scenarios. Allows
    /// customization of serialization behavior.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// The optional delegate used to create a <see cref="BlobServiceClient"/> instance.
    /// </summary>
    internal Func<CancellationToken, Task<BlobServiceClient>> CreateClient { get; private set; } = _ => throw new InvalidOperationException("CreateClient not configured.");

    /// <summary>
    /// Stage of silo lifecycle where storage should be initialized.  Storage must be initialized prior to use.
    /// </summary>
    public int InitStage { get; set; } = DefaultInitStage;

    /// <summary>
    /// A function for building container factory instances.
    /// </summary>
    public Func<IServiceProvider, AzureAppendBlobEventStorageOptions, IBlobContainerFactory> BuildContainerFactory { get; set; }
        = static (provider, options) => new DefaultBlobContainerFactory(options);

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using a connection string.
    /// </summary>
    public void ConfigureBlobServiceClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using an authenticated service URI.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, ClientOptions));
    }

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using the provided callback.
    /// </summary>
    public void ConfigureBlobServiceClient(Func<CancellationToken, Task<BlobServiceClient>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
    }

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using an authenticated service URI and a <see cref="TokenCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, TokenCredential tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(tokenCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using an authenticated service URI and a <see cref="AzureSasCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(azureSasCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, azureSasCredential, ClientOptions));
    }

    /// <summary>
    /// Configures the <see cref="BlobServiceClient"/> using an authenticated service URI and a <see cref="StorageSharedKeyCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(sharedKeyCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
    }
}
