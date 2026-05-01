namespace Egil.Orleans.Testing;

/// <summary>
/// Identifies the kind of grain activity observed by the testing collector.
/// </summary>
public enum GrainActivityKind
{
    /// <summary>
    /// A grain call was observed.
    /// </summary>
    GrainCall,

    /// <summary>
    /// A grain state write operation was observed.
    /// </summary>
    StorageWrite,

    /// <summary>
    /// A grain state read operation was observed.
    /// </summary>
    StorageRead,

    /// <summary>
    /// A grain state clear operation was observed.
    /// </summary>
    StorageClear,
}
