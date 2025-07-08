namespace Egil.Orleans.EventSourcing.Internals.Storage;

public class OptimisticConcurrencyException : Exception
{
    public OptimisticConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
