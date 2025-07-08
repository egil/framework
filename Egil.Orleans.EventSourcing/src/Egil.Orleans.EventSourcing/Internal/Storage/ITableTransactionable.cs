using Azure.Data.Tables;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface ITableTransactionable
{
    IEnumerable<TableTransactionAction> ToTableTransactionAction(long startingSequenceNumber);
}
