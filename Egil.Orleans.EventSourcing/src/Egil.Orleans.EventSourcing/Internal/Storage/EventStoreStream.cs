using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventStoreStream
{
    public required string StreamName { get; init; }

    public required ImmutableArray<ITableTransactionable> Events { get; init; }

    public required EventStreamRetention Retention { get; init; }
}
