namespace Egil.Orleans.EventSourcing.Internals.Storage;

public class DuplicateEventException : Exception
{
    public DuplicateEventException(string message, Exception innerException)
        : base(message, innerException) { }
}