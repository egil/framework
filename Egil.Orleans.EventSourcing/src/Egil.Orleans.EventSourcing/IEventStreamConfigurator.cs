namespace Egil.Orleans.EventSourcing.EventStores;

internal interface IEventStreamConfigurator
{
    string StreamName { get; }

    IEventStream Build();
}
