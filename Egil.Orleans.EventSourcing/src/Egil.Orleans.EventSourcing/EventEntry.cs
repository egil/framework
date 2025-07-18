using Azure;
using Orleans.Storage;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing;

internal record class EventEntry<TEvent> : IEventEntry<TEvent>, IEventEntry
    where TEvent : notnull
{
    public required TEvent Event { get; init; }

    public required string StreamName { get; init; }

    public required long SequenceNumber { get; init; }

    public string? EventId { get; init; }

    public DateTimeOffset? EventTimestamp { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public ETag ETag { get; init; } = ETag.All;

    public ImmutableDictionary<string, ReactorState> ReactorStatus { get; init; } = ImmutableDictionary<string, ReactorState>.Empty;

    object IEventEntry.Event => Event;

    public TRequestedEvent? TryCastEvent<TRequestedEvent>() where TRequestedEvent : notnull
        => Event is TRequestedEvent requestedEvent
        ? requestedEvent
        : default;

    public IEventEntry SetReactorStatus(string reactorId, ReactorState state)
    {
        return this with { ReactorStatus = ReactorStatus.SetItem(reactorId, state) };
    }

    public BinaryData Serialize(IGrainStorageSerializer serializer)
        => serializer.Serialize(Event);
}
