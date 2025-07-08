namespace Egil.Orleans.EventSourcing.Internals.Storage;

internal enum ReactorOperationStatus
{
    NotStarted = 0,
    CompleteSuccessful = 1,
    Failed = 2,
    Abandoned = 3
}
