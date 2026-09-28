namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Thrown when an operation attempts to change or reload a fenced manager.
/// </summary>
/// <remarks>
/// The failed storage operation throws its original exception. Later mutations,
/// storage reads and hook configuration throw this exception with that failure as
/// its inner exception. Local state inspection remains available, but neither the
/// snapshot nor this classification establishes the current durable state.
/// </remarks>
[GenerateSerializer]
[Alias("egil.orleans.messaging.StateManagerFencedException")]
public sealed class StateManagerFencedException : InvalidOperationException
{
    /// <summary>Creates a fencing exception retaining the original storage failure.</summary>
    public StateManagerFencedException(StorageFailureKind failureKind, Exception innerException)
        : base($"The state manager is fenced after a storage mutation failed ({failureKind}). " +
            "Recover through a new activation or reload storage before constructing a new manager.", innerException)
    {
        FailureKind = failureKind;
    }

    /// <summary>Gets the classification of the storage operation that fenced the manager.</summary>
    [Id(0)]
    public StorageFailureKind FailureKind { get; }
}
