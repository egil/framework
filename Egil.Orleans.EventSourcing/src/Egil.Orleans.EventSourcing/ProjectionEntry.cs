using Azure;

namespace Egil.Orleans.EventSourcing;

internal record class ProjectionEntry<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    public required TProjection Projection { get; init; }

    public required long EventSequenceNumber { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public ETag ETag { get; init; } = ETag.All;

    public static ProjectionEntry<TProjection> CreateDefault() =>
        new ProjectionEntry<TProjection>
        {
            Projection = TProjection.CreateDefault(),
            EventSequenceNumber = 0L,
            Timestamp = null,
            ETag = ETag.All
        };
}
