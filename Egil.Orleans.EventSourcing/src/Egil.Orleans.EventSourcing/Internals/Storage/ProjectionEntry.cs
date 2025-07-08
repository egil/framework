using Azure;

namespace Egil.Orleans.EventSourcing.Internals.Storage;

internal readonly record struct ProjectionEntry<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    public required TProjection Projection { get; init; }

    public required long NextEventSequenceNumber { get; init; }

    public required long StreamEventCount { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required ETag ETag { get; init; }
}
