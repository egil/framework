using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Streamstone;

namespace Egil.Orleans.EventSourcing.AzureStorage.TableStorage;

internal sealed class StreamstoneEventStorageProvider(
    IOptions<AzureTableEventStorageOptions> options,
    ILoggerFactory loggerFactory) : IEventStorageProvider, ILifecycleParticipant<ISiloLifecycle>
{
    private readonly AzureTableEventStorageOptions options = options.Value;
    private TableClient? table;

    [MemberNotNull(nameof(table))]
    private async Task Initialize(CancellationToken cancellationToken)
    {
        table = options.TableServiceClient.GetTableClient(options.TableName);
        await table.CreateIfNotExistsAsync(cancellationToken);
    }

    public IEventStorage<TEvent> Create<TEvent>(IGrainContext grainContext)
    {
        if (table is null)
        {
            throw new InvalidOperationException("StreamstoneEventStorageProvider has not been initialized yet.");
        }

        return new StreamstoneEventStorage<TEvent>(
            new Partition(
                table,
                grainContext.GrainId.ToString()),
            options.JsonSerializerOptions,
            loggerFactory.CreateLogger<StreamstoneEventStorage<TEvent>>());
    }

    public void Participate(ISiloLifecycle observer)
    {
        observer.Subscribe(
            nameof(StreamstoneEventStorageProvider),
            options.InitStage,
            onStart: Initialize);
    }
}
