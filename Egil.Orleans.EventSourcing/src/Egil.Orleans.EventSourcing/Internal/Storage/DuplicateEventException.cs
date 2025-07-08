namespace Egil.Orleans.EventSourcing.Internal;

public class DuplicateEventException : Exception
{
    public DuplicateEventException(string message, Exception innerException)
        : base(message, innerException) { }
}