using Azure;
using Orleans.Storage;

namespace Egil.Orleans.Messaging.State.AzureStorage;

/// <summary>
/// Azure Table/Blob storage-aware <see cref="IStateManager{T}"/>.
/// </summary>
/// <typeparam name="T">The grain state type.</typeparam>
public sealed class AzureStorageStateManager<T> : StateManagerBase<T>
    where T : class, IEquatable<T>
{
    /// <summary>
    /// Creates a state manager around an Orleans Azure Storage-backed state facet.
    /// </summary>
    public AzureStorageStateManager(IPersistentState<T> storage)
        : base(storage)
    {
    }

    /// <inheritdoc/>
    protected override StorageFailureKind ClassifyWriteFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AzureStorageFailureClassifier.Classify(exception);
    }

    /// <inheritdoc/>
    protected override StorageFailureKind ClassifyClearFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AzureStorageFailureClassifier.Classify(exception);
    }
}

/// <summary>
/// Factory that creates <see cref="AzureStorageStateManager{T}"/> instances.
/// </summary>
public sealed class AzureStorageStateManagerFactory<T> : IStateManagerFactory<T>
    where T : class, IEquatable<T>
{
    /// <inheritdoc/>
    public IStateManager<T> Create(IPersistentState<T> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new AzureStorageStateManager<T>(storage);
    }
}

internal static class AzureStorageFailureClassifier
{
    public static StorageFailureKind Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InconsistentStateException)
            {
                return StorageFailureKind.DidNotPersist;
            }

            if (current is RequestFailedException requestFailed
                && requestFailed.Status is 412)
            {
                return StorageFailureKind.DidNotPersist;
            }
        }

        return StorageFailureKind.UnknownOutcome;
    }
}