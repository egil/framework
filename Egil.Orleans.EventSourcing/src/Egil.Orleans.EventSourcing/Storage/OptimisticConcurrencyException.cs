namespace Egil.Orleans.EventSourcing.Storage;

public class OptimisticConcurrencyException : Exception
{
    public OptimisticConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
