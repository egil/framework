using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Storage;

namespace Egil.Orleans.EventSourcing.Storage;

internal class AzureTableEventStoreFactory : IEventStoreFactory
{
    public IEventStore<TProjection> CreateEventStore<TProjection>(IServiceProvider serviceProvider) where TProjection : notnull, IEventProjection<TProjection>
    {
        var tableClient = serviceProvider.GetRequiredService<TableClient>();
        var serializer = serviceProvider.GetRequiredService<IGrainStorageSerializer>();
        var options = serviceProvider.GetRequiredService<IOptions<ClusterOptions>>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        return new AzureTableEventStore<TProjection>(tableClient, serializer, options, timeProvider);
    }
}
