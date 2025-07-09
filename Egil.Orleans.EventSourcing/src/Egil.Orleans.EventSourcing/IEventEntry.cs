using Azure;
using Azure.Data.Tables;
using Orleans.Storage;

namespace Egil.Orleans.EventSourcing;

public interface IEventEntry
{
    long SequenceNumber { get; }

    string? EventId { get; }

    DateTimeOffset? EventTimestamp { get; }

    DateTimeOffset? Timestamp { get; set; }

    ETag ETag { get; set; }

    object Event { get; }
}

public interface IEventEntry<TEvent> : IEventEntry
    where TEvent : notnull
{
    new TEvent Event { get; }
}