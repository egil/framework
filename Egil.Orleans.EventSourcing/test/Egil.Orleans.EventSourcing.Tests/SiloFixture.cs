using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Egil.Orleans.EventSourcing.Tests;

public sealed class SiloFixture : IAsyncLifetime, IGrainFactory
{
    private InProcessTestCluster? cluster;
    private IGrainFactory? grainFactory;

    public IServiceProvider Services => cluster?.GetSiloServiceProvider() ?? throw new InvalidOperationException("Test cluster not running.");

    public FakeEventStorage EventStorage { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((options, siloBuilder) =>
        {
            siloBuilder.Services.AddSingleton<IEventStorage>(EventStorage);
            siloBuilder.UseInMemoryReminderService();
        });

        builder.ConfigureClient((clientBuilder) =>
        {
        });

        cluster = builder.Build();
        await cluster.DeployAsync();
        grainFactory = cluster.Client;
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
        {
            await cluster.StopAllSilosAsync();
        }
    }

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey => grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => grainFactory.CreateObjectReference<TGrainObserverInterface>(obj);
    public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => grainFactory.DeleteObjectReference<TGrainObserverInterface>(obj);
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, keyExtension);
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, keyExtension);
    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => grainFactory.GetGrain<TGrainInterface>(grainId);
    public IAddressable GetGrain(GrainId grainId) => grainFactory.GetGrain(grainId);
    public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => grainFactory.GetGrain(grainId, interfaceType);
}