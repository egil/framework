using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Egil.Orleans.EventSourcing.Tests;

public sealed class SiloFixture : IAsyncLifetime, IGrainFactory
{
    private InProcessTestCluster? cluster;
    private IGrainFactory? grainFactory;

    public IServiceProvider Services => cluster?.GetSiloServiceProvider() ?? throw new InvalidOperationException("Test cluster not running.");

    public FakeEventStorage EventStorage { get; } = new();

    public IGrainFactory GrainFactory => grainFactory ?? throw new InvalidOperationException("Test cluster not running.");

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((options, siloBuilder) =>
        {
            siloBuilder.Services.AddSingleton<IEventStore>(EventStorage);
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

    #region IGrainFactory methods
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => GrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => GrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey => GrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => GrainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => GrainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => GrainFactory.CreateObjectReference<TGrainObserverInterface>(obj);
    public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => GrainFactory.DeleteObjectReference<TGrainObserverInterface>(obj);
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => GrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => GrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => GrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => GrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, keyExtension);
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => GrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, keyExtension);
    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => GrainFactory.GetGrain<TGrainInterface>(grainId);
    public IAddressable GetGrain(GrainId grainId) => GrainFactory.GetGrain(grainId);
    public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => GrainFactory.GetGrain(grainId, interfaceType);
    #endregion
}