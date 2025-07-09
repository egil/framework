using Azure;
using Azure.Data.Tables;
using Orleans.Storage;

namespace Egil.Orleans.EventSourcing.Storage;

public interface IEventEntry
{
    TableTransactionAction ToTableTransactionAction(string partitionKey, string rowKey, IGrainStorageSerializer serializer);

    long SequenceNumber { get; }

    string? EventId { get; }

    DateTimeOffset? EventTimestamp { get; }

    DateTimeOffset? Timestamp { get; set; }

    ETag ETag { get; set; }

    TEvent? TryCastEvent<TEvent>() where TEvent : notnull;
}
