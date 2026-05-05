using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorBuilderTests
{
    [Fact]
    public void AddGrainActivityCollector_throws_for_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GrainActivityCollectorSiloBuilderExtensions.AddGrainActivityCollector(null!, new GrainActivityCollector()));
    }

    [Fact]
    public void AddGrainActivityCollector_throws_for_null_collector()
    {
        var builder = new TestSiloBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddGrainActivityCollector(null!));
    }

    [Fact]
    public void AddGrainActivityCollector_registers_collector_and_returns_builder()
    {
        var builder = new TestSiloBuilder();
        var collector = new GrainActivityCollector();

        var result = builder.AddGrainActivityCollector(collector);

        Assert.NotNull(result);
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(GrainActivityCollector) && ReferenceEquals(descriptor.ImplementationInstance, collector));
    }

    [Fact]
    public void CollectStorageActivityFrom_throws_when_named_provider_is_missing()
    {
        var builder = new TestSiloBuilder();
        builder.Services.AddSingleton<IGrainStorage>(new FakeGrainStorage());
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Other", new FakeGrainStorage()));

        var collectorBuilder = builder.AddGrainActivityCollector(new GrainActivityCollector());
        var exception = Assert.Throws<InvalidOperationException>(() => collectorBuilder.CollectStorageActivityFrom("Default"));

        Assert.Contains("Default", exception.Message);
    }

    [Fact]
    public void CollectStorageActivityFrom_throws_for_null_provider_name()
    {
        var builder = new TestSiloBuilder();
        var collectorBuilder = builder.AddGrainActivityCollector(new GrainActivityCollector());

        Assert.Throws<ArgumentNullException>(() => collectorBuilder.CollectStorageActivityFrom(null!));
    }

    [Fact]
    public void CollectStorageActivityFromDefault_clones_instance_registration_once()
    {
        var builder = new TestSiloBuilder();
        var storage = new FakeGrainStorage();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Default", storage));

        var collectorBuilder = builder.AddGrainActivityCollector(new GrainActivityCollector());

        Assert.Same(collectorBuilder, collectorBuilder.CollectStorageActivityFromDefault());
        Assert.Same(storage, GetSingleDescriptor(builder.Services, "Egil.Orleans.Testing.Inner::Default").KeyedImplementationInstance);
        Assert.NotNull(GetSingleDescriptor(builder.Services, "Default").KeyedImplementationFactory);

        collectorBuilder.CollectStorageActivityFromDefault();

        Assert.Single(GetDescriptors(builder.Services, "Default"));
        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Default"));
    }

    [Fact]
    public async Task AddGrainActivityCollector_registers_framework_services_and_resolves_decorated_storage()
    {
        var builder = new TestSiloBuilder();
        var collector = new GrainActivityCollector();
        var storage = new FakeGrainStorage();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Default", storage));
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(object), "Default", new object()));

        builder.AddGrainActivityCollector(collector)
            .CollectStorageActivityFrom("Default");

        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(IIncomingGrainCallFilter));

        using var provider = builder.Services.BuildServiceProvider();
        var decoratedStorage = provider.GetRequiredKeyedService<IGrainStorage>("Default");
        var grainId = GrainId.Create("test-grain", "builder");
        var state = new TestGrainState<string> { ETag = "etag-1", State = "value" };
        var ct = TestContext.Current.CancellationToken;

        var collectTask = collector
            .GetStorageOperationsAsync(cancellationToken: ct)
            .Where(op => op.StorageName == "Default" && op.GrainId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        Assert.Same(collector, provider.GetRequiredService<GrainActivityCollector>());
        Assert.NotSame(storage, decoratedStorage);

        await decoratedStorage.WriteStateAsync("state", grainId, state);
        var collected = await collectTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);

        Assert.Single(collected);
        Assert.Equal(1, storage.WriteCount);
        Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(object) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, "Default"));
    }

    [Fact]
    public void CollectStorageActivityFrom_preserves_type_registration_when_cloning()
    {
        var builder = new TestSiloBuilder();
        builder.Services.Add(ServiceDescriptor.DescribeKeyed(typeof(IGrainStorage), "TypeBased", typeof(FakeGrainStorage), ServiceLifetime.Singleton));

        builder.AddGrainActivityCollector(new GrainActivityCollector())
            .CollectStorageActivityFrom("TypeBased");

        Assert.Equal(typeof(FakeGrainStorage), GetSingleDescriptor(builder.Services, "Egil.Orleans.Testing.Inner::TypeBased").KeyedImplementationType);
    }

    [Fact]
    public void CollectStorageActivityFrom_preserves_factory_registration_when_cloning()
    {
        var builder = new TestSiloBuilder();
        Func<IServiceProvider, object?, object> factory = static (_, _) => new FakeGrainStorage();
        builder.Services.Add(ServiceDescriptor.DescribeKeyed(typeof(IGrainStorage), "FactoryBased", factory, ServiceLifetime.Singleton));

        builder.AddGrainActivityCollector(new GrainActivityCollector())
            .CollectStorageActivityFrom("FactoryBased");

        Assert.Same(factory, GetSingleDescriptor(builder.Services, "Egil.Orleans.Testing.Inner::FactoryBased").KeyedImplementationFactory);
    }

    [Fact]
    public async Task CollectStorageActivityFrom_preserves_factory_registration_when_resolving_decorated_storage()
    {
        var builder = new TestSiloBuilder();
        var collector = new GrainActivityCollector();
        var storage = new FakeGrainStorage();
        object? resolvedKey = null;
        var factoryCallCount = 0;
        builder.Services.Add(ServiceDescriptor.DescribeKeyed(
            typeof(IGrainStorage),
            "FactoryBased",
            (serviceProvider, key) =>
            {
                factoryCallCount++;
                resolvedKey = key;
                Assert.Same(collector, serviceProvider.GetRequiredService<GrainActivityCollector>());
                return storage;
            },
            ServiceLifetime.Singleton));

        builder.AddGrainActivityCollector(collector)
            .CollectStorageActivityFrom("FactoryBased");

        using var provider = builder.Services.BuildServiceProvider();
        var decoratedStorage = provider.GetRequiredKeyedService<IGrainStorage>("FactoryBased");
        var grainId = GrainId.Create("test-grain", "factory-based");
        var state = new TestGrainState<string> { ETag = "etag-1", State = "value" };
        var ct = TestContext.Current.CancellationToken;

        var collectTask = collector
            .GetStorageOperationsAsync(cancellationToken: ct)
            .Where(op => op.StorageName == "FactoryBased" && op.GrainId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await decoratedStorage.WriteStateAsync("state", grainId, state);
        var collected = await collectTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);

        Assert.Single(collected);

        Assert.NotSame(storage, decoratedStorage);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal("Egil.Orleans.Testing.Inner::FactoryBased", resolvedKey);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void CollectStorageActivity_discovers_all_keyed_providers()
    {
        var builder = new TestSiloBuilder();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Default", new FakeGrainStorage()));
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Archive", new FakeGrainStorage()));

        builder.AddGrainActivityCollector(new GrainActivityCollector())
            .CollectStorageActivity();

        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Default"));
        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Archive"));
        Assert.NotNull(GetSingleDescriptor(builder.Services, "Default").KeyedImplementationFactory);
        Assert.NotNull(GetSingleDescriptor(builder.Services, "Archive").KeyedImplementationFactory);
    }

    [Fact]
    public void CollectStorageActivity_skips_inner_registrations()
    {
        var builder = new TestSiloBuilder();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Default", new FakeGrainStorage()));
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Egil.Orleans.Testing.Inner::Default", new FakeGrainStorage()));

        builder.AddGrainActivityCollector(new GrainActivityCollector())
            .CollectStorageActivity();

        // The manually added inner registration should not be decorated again
        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Default"));
    }

    [Fact]
    public void CollectStorageActivity_is_idempotent()
    {
        var builder = new TestSiloBuilder();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Default", new FakeGrainStorage()));

        var collectorBuilder = builder.AddGrainActivityCollector(new GrainActivityCollector());
        collectorBuilder.CollectStorageActivity();
        collectorBuilder.CollectStorageActivity();

        Assert.Single(GetDescriptors(builder.Services, "Default"));
        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Default"));
    }

    [Fact]
    public void CollectStorageActivity_ignores_non_keyed_registrations()
    {
        var builder = new TestSiloBuilder();
        // Non-keyed registration — should not be discovered
        builder.Services.AddSingleton<IGrainStorage>(new FakeGrainStorage());
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), "Named", new FakeGrainStorage()));

        builder.AddGrainActivityCollector(new GrainActivityCollector())
            .CollectStorageActivity();

        // Only the keyed "Named" provider should be decorated
        Assert.Single(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::Named"));
        Assert.Empty(GetDescriptors(builder.Services, "Egil.Orleans.Testing.Inner::"));
    }

    [Fact]
    public void CollectStorageActivity_with_no_providers_is_noop()
    {
        var builder = new TestSiloBuilder();

        var collectorBuilder = builder.AddGrainActivityCollector(new GrainActivityCollector());

        // Should not throw
        collectorBuilder.CollectStorageActivity();

        Assert.DoesNotContain(builder.Services, d =>
            d.ServiceType == typeof(IGrainStorage) && d.IsKeyedService &&
            d.ServiceKey is string key && key.StartsWith("Egil.Orleans.Testing.Inner::", StringComparison.Ordinal));
    }

    private static ServiceDescriptor GetSingleDescriptor(IServiceCollection services, object key)
        => Assert.Single(GetDescriptors(services, key));

    private static IEnumerable<ServiceDescriptor> GetDescriptors(IServiceCollection services, object key)
        => services.Where(descriptor => descriptor.ServiceType == typeof(IGrainStorage) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, key));

    private sealed class FakeGrainStorage : IGrainStorage
    {
        public int WriteCount { get; private set; }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
    }

    private sealed class TestGrainState<T> : IGrainState<T>
    {
        public string? ETag { get; set; }

        public bool RecordExists { get; set; }

        public T State { get; set; } = default!;
    }
}
