namespace Egil.Orleans.Testing;

/// <summary>
/// Identifies the kind of grain storage operation that was observed.
/// </summary>
public enum StorageOperationKind
{
    /// <summary>
    /// A state clear operation was observed.
    /// </summary>
    Clear,

    /// <summary>
    /// A state read operation was observed.
    /// </summary>
    Read,

    /// <summary>
    /// A state write operation was observed.
    /// </summary>
    Write,
}
