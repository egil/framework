using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Streamstone;

namespace Egil.Orleans.EventSourcing.Tests;

public sealed class AppHostFixture : IAsyncLifetime
{
    private IDistributedApplicationTestingBuilder? appHost;
    private DistributedApplication? app;

    public string BlobStorageConnectionString { get; private set; } = string.Empty;

    public string TableStorageConnectionString { get; private set; } = string.Empty;

    public ILoggerFactory LoggerFactory { get; private set; }

    public AppHostFixture()
    {
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
            .AddProvider(new XUnitLoggerProvider(new TestOutputHelperAccessor(), new XUnitLoggerOptions() { IncludeScopes = true }))
            .SetMinimumLevel(LogLevel.Trace));
    }

    public async ValueTask InitializeAsync()
    {
        appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Egil_Orleans_EventSourcing_Tests_AppHost>(TestContext.Current.CancellationToken);
        app = await appHost.BuildAsync(TestContext.Current.CancellationToken);
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await resourceNotificationService.WaitForResourceAsync("blobStorage", KnownResourceStates.Running, TestContext.Current.CancellationToken);
        BlobStorageConnectionString = await app.GetConnectionStringAsync("blobStorage", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Failed to get blobStorage connection string");
        await resourceNotificationService.WaitForResourceAsync("tableStorage", KnownResourceStates.Running, TestContext.Current.CancellationToken);
        TableStorageConnectionString = await app.GetConnectionStringAsync("tableStorage", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Failed to get tableStorage connection string");
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }

        if (appHost is not null)
        {
            await appHost.DisposeAsync();
        }
    }

    public async Task<AppendBlobClient> GetAppendBlobClientAsync(string containerName = "logs", string? blobName = null)
    {
        var blobServiceClient = new BlobServiceClient(BlobStorageConnectionString);
        var defaultContainer = blobServiceClient.GetBlobContainerClient("logs");
        await defaultContainer.CreateIfNotExistsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var appendBlobClient = defaultContainer.GetAppendBlobClient(blobName ?? Guid.CreateVersion7().ToString("N"));
        return appendBlobClient;
    }

    public async Task<Partition> GetPartitionAsync(string tableName = "logs", string? blobName = null)
    {
        var tableClient = new TableServiceClient(TableStorageConnectionString).GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync(cancellationToken: TestContext.Current.CancellationToken);
        return new Partition(
            tableClient,
            blobName ?? Guid.CreateVersion7().ToString("N"));
    }

    private sealed class TestOutputHelperAccessor : ITestOutputHelperAccessor
    {
        public ITestOutputHelper? OutputHelper
        {
            get => TestContext.Current.TestOutputHelper;
            set
            {
                throw new NotImplementedException();
            }
        }
    }
}
