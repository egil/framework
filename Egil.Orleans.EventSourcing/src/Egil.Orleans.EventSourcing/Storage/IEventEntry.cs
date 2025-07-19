using Azure;
using Orleans.Storage;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Storage;

internal interface IEventEntry
{
    string StreamName { get; }

    long SequenceNumber { get; }

    string? EventId { get; }

    DateTimeOffset? EventTimestamp { get; }

    DateTimeOffset? Timestamp { get; }

    ETag ETag { get; }

    object Event { get; }

    ImmutableDictionary<string, ReactorState> ReactorStatus { get; }

    IEventEntry SetReactorStatus(string reactorId, ReactorState state);

    BinaryData Serialize(IGrainStorageSerializer serializer);
}

internal interface IEventEntry<out TEvent> : IEventEntry where TEvent : notnull
{
    new TEvent Event { get; }
}