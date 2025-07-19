namespace Egil.Orleans.EventSourcing;

internal readonly record struct ReactorState(
    string ReactorId,
    int Attempts,
    ReactorOperationStatus Status,
    DateTimeOffset Timestamp)
{
    public static ReactorState Create(string reactorId)
    {
        return new ReactorState(reactorId, 0, ReactorOperationStatus.NotStarted, DateTimeOffset.MinValue);
    }
}
