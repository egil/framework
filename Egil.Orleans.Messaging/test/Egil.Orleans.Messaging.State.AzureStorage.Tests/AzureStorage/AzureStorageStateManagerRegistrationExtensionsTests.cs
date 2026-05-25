namespace Egil.Orleans.Messaging.State.AzureStorage.Tests.AzureStorage;

public sealed class AzureStorageStateManagerRegistrationExtensionsTests
{
    [Fact]
    public void AddAzureStorageStateManager_registers_keyed_factory()
    {
        var services = new ServiceCollection();

        services.AddAzureStorageStateManager("state");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<IStateManagerFactory<TestState>>("state");

        Assert.IsType<AzureStorageStateManagerFactory<TestState>>(factory);
    }

    [Fact]
    public void AddAzureStorageStateManager_throws_for_empty_storage_name()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddAzureStorageStateManager(""));

        Assert.Equal("storageName", exception.ParamName);
    }

    private sealed record TestState(string Value);
}