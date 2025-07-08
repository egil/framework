using Orleans.Runtime;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventStoreSaveOperation<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    public required GrainId GrainId { get; init; }

    public required ProjectionEntry<TProjection> Projection { get; init; }

    public required ImmutableArray<EventStoreStream> Streams { get; init; }
}
