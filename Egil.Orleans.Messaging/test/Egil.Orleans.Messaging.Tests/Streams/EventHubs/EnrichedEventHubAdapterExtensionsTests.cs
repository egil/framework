namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

public sealed class EnrichedEventHubAdapterExtensionsTests
{
    [Fact]
    public void UseEnrichedDataAdapter_throws_for_null_configurator()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            EnrichedEventHubAdapterExtensions.UseEnrichedDataAdapter(null!));

        Assert.Equal("configurator", ex.ParamName);
    }

    [Fact]
    public void UseEnrichedDataAdapter_registers_configuration_callback()
    {
        var configurator = new FakeEventHubStreamConfigurator("provider-a");

        configurator.UseEnrichedDataAdapter();

        Assert.Equal(1, configurator.ConfigureCalls);
        Assert.NotNull(configurator.LastConfigureAction);
    }

    private sealed class FakeEventHubStreamConfigurator(string name) : IEventHubStreamConfigurator
    {
        public int ConfigureCalls { get; private set; }
        public Action<IServiceCollection>? LastConfigureAction { get; private set; }

        public string Name => name;

        public Action<Action<IServiceCollection>> ConfigureDelegate => configure =>
        {
            ConfigureCalls++;
            LastConfigureAction = configure;
        };
    }
}
