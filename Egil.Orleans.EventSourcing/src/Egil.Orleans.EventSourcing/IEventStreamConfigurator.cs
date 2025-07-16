namespace Egil.Orleans.EventSourcing;

internal interface IEventStreamConfigurator
{
    string StreamName { get; }

    IEventStream Build();
}
