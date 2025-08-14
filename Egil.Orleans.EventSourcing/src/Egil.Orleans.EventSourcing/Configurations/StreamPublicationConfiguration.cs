namespace Egil.Orleans.EventSourcing.Configurations;

internal class StreamPublicationConfiguration
{
    public required Type EventType { get; init; }
    public required string StreamProvider { get; init; }
    public required string StreamNamespace { get; init; }
    public required StreamKeySelector? KeySelector { get; init; }
}