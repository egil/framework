using System.Text.Json;
using Orleans.TestingHost;

namespace Egil.Orleans.StateMigration.Tests.SystemTextJson;

public sealed class OrleansInProcessClusterFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public SystemTextJsonGrainStorageSerializer DefaultSerializer { get; } = CreateDefaultSerializer();
    public SystemTextJsonGrainStorageSerializer CustomSerializer { get; } = CreateCustomSerializer();

    public async ValueTask DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
    }

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("storage-default", optionsBuilder =>
            {
                optionsBuilder.Configure(options => options.GrainStorageSerializer = DefaultSerializer);
            });

            siloBuilder.AddMemoryGrainStorage("storage-custom", optionsBuilder =>
            {
                optionsBuilder.Configure(options => options.GrainStorageSerializer = CustomSerializer);
            });
        });

        InProcessTestCluster cluster = builder.Build();
        await cluster.DeployAsync();
        Cluster = cluster;
    }

    private static SystemTextJsonGrainStorageSerializer CreateDefaultSerializer()
    {
        JsonSerializerOptions readAndWriteOptions = new JsonSerializerOptions().AddStateMigrationSupport();
        JsonSerializerOptions flattenedWriteOptions = new JsonSerializerOptions()
            .AddStateMigrationSupport(StoragePayloadLayout.Flattened);
        return new SystemTextJsonGrainStorageSerializer(readAndWriteOptions, flattenedWriteOptions);
    }

    private static SystemTextJsonGrainStorageSerializer CreateCustomSerializer()
    {
        JsonSerializerOptions customOptions = new JsonSerializerOptions()
            .AddStateMigrationSupport("_type", "_value");
        return new SystemTextJsonGrainStorageSerializer(customOptions, flattenedWriteOptions: null);
    }
}
