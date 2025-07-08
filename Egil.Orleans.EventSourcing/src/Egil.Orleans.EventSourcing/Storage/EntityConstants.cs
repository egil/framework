namespace Egil.Orleans.EventSourcing.Storage;

internal static class EntityConstants
{
    internal const string DataColumnName = "Data";
    internal const string NextEventSequenceNumberColumnName = "NextEventSequenceNumber";
    internal const string StreamEventCountColumnName = "StreamEventCount";
    internal const string EventIdColumnName = "EventId";
    internal const string ReactorStatusColumnName = "ReactorStatus";
    internal const string SequenceNumberColumnName = "SequenceNumber";
    internal const string EventTimestampColumnName = "EventTimestamp";
}
