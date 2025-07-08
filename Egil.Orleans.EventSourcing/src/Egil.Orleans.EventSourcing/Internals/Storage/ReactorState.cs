namespace Egil.Orleans.EventSourcing.Internals.Storage;

internal readonly record struct ReactorState(
    string ReactorId,
    int Attempts,
    ReactorOperationStatus Status,
    DateTimeOffset Timestamp);
