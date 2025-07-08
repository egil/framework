namespace Egil.Orleans.EventSourcing.Internal;

public class OptimisticConcurrencyException : Exception
{
    public OptimisticConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
