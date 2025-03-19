namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents a projection of a state that has been derived from a series of events.
/// </summary>
public sealed class Projection<TState>
{
    public TState? State { get; init; }

    public int Version { get; init; }

    public required int MetadataHashCode { get; init; }
}
