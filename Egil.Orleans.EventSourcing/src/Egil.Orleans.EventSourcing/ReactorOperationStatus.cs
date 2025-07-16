namespace Egil.Orleans.EventSourcing;

public enum ReactorOperationStatus
{
    NotStarted = 0,
    CompleteSuccessful = 1,
    Failed = 2,
}
