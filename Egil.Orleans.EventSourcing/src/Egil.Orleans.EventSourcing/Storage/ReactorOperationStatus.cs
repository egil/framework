namespace Egil.Orleans.EventSourcing.Storage;

internal enum ReactorOperationStatus
{
    NotStarted = 0,
    CompleteSuccessful = 1,
    Failed = 2,
}
