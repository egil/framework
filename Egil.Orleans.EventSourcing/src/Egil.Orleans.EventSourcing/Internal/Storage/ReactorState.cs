namespace Egil.Orleans.EventSourcing.Internal;

internal readonly record struct ReactorState(
    string ReactorId,
    int Attempts,
    ReactorOperationStatus Status,
    DateTimeOffset Timestamp);
