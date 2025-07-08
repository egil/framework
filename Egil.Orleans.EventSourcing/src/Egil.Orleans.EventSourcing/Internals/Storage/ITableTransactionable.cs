using Azure.Data.Tables;

namespace Egil.Orleans.EventSourcing.Internals.Storage;

internal interface ITableTransactionable
{
    IEnumerable<TableTransactionAction> ToTableTransactionAction(long startingSequenceNumber);
}
