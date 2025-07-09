namespace Egil.Orleans.EventSourcing;

internal static class EntityConstants
{
    internal const string DataColumnName = "Data";
    internal const string NextEventSequenceNumberColumnName = "NextEventSequenceNumber";
    internal const string StoreEventCountColumnName = "StoreEventCount";
    internal const string StreamEventCountColumnName = "StreamEventCount";
    internal const string EventIdColumnName = "EventId";
    internal const string ReactorStatusColumnName = "ReactorStatus";
    internal const string SequenceNumberColumnName = "SequenceNumber";
    internal const string EventTimestampColumnName = "EventTimestamp";
    internal const string StreamNameColumnName = "StreamName";
    internal const string LatestEventTimestampColumnName = "LatestEventTimestamp";
    internal const string OldestEventTimestampColumnName = "OldestEventTimestamp";

}
