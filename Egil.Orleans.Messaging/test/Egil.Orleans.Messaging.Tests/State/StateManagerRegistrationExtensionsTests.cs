namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerRegistrationExtensionsTests
{
    [Fact]
    public void AddDefaultStateManager_registers_keyed_default_factory_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddDefaultStateManager("state");

        var descriptor = services.Single(service =>
            service.ServiceType == typeof(IStateManagerFactory<>)
            && Equals(service.ServiceKey, "state"));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = services.BuildServiceProvider();
        var first = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");
        var second = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.IsType<DefaultStateManagerFactory<TestState>>(first);
    }

    [Fact]
    public void AddStateManagerFactory_registers_custom_open_generic_factory_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddStateManagerFactory("state", typeof(CustomStateManagerFactory<>));

        var descriptor = services.Single(service =>
            service.ServiceType == typeof(IStateManagerFactory<>)
            && Equals(service.ServiceKey, "state"));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = services.BuildServiceProvider();
        var first = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");
        var second = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.IsType<CustomStateManagerFactory<TestState>>(first);
    }

    [Fact]
    public void AddStateManagerFactory_registers_custom_typed_factory_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddStateManagerFactory<TestState, TestStateManagerFactory>("state");

        var descriptor = services.Single(service =>
            service.ServiceType == typeof(IStateManagerFactory<TestState>)
            && Equals(service.ServiceKey, "state"));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = services.BuildServiceProvider();
        var first = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");
        var second = provider.GetKeyedService<IStateManagerFactory<TestState>>("state");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.IsType<TestStateManagerFactory>(first);
    }

    [Fact]
    public void AddStateManagerFactory_throws_for_invalid_open_generic_type()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddStateManagerFactory("state", typeof(string)));

        Assert.Equal("openGenericFactoryType", ex.ParamName);
    }

    [Fact]
    public void AddDefaultStateManager_throws_for_empty_storage_name()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddDefaultStateManager(" "));

        Assert.Equal("storageName", ex.ParamName);
    }

    [Fact]
    public void ISiloBuilder_extensions_delegate_to_service_collection_registration()
    {
        var builder = new FakeSiloBuilder();

        builder
            .AddDefaultStateManager("default-state")
            .AddStateManagerFactory("custom-state", typeof(CustomStateManagerFactory<>))
            .AddStateManagerFactory<TestState, TestStateManagerFactory>("typed-state");

        var provider = builder.Services.BuildServiceProvider();
        Assert.IsType<DefaultStateManagerFactory<TestState>>(
            provider.GetKeyedService<IStateManagerFactory<TestState>>("default-state"));
        Assert.IsType<CustomStateManagerFactory<TestState>>(
            provider.GetKeyedService<IStateManagerFactory<TestState>>("custom-state"));
        Assert.IsType<TestStateManagerFactory>(
            provider.GetKeyedService<IStateManagerFactory<TestState>>("typed-state"));
    }

    private sealed record TestState(string Value) : IEquatable<TestState>;

    private sealed class CustomStateManagerFactory<TState> : IStateManagerFactory<TState>
        where TState : class, IEquatable<TState>
    {
        public IStateManager<TState> Create(IPersistentState<TState> storage) =>
            new DefaultStateManager<TState>(storage);
    }

    private sealed class TestStateManagerFactory : IStateManagerFactory<TestState>
    {
        public IStateManager<TestState> Create(IPersistentState<TestState> storage) =>
            new DefaultStateManager<TestState>(storage);
    }

    private sealed class FakeSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; } =
            new Microsoft.Extensions.Configuration.ConfigurationManager();
    }
}
