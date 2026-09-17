namespace Egil.Orleans.Messaging.Streams.EventHubs.Tests.EventHubs;

internal sealed class FakeEventHubStreamConfigurator(string name) : IEventHubStreamConfigurator
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
