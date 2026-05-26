using Azure;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs.Models;
using System.Net;
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
        return TryClassify(exception) ?? StorageFailureKind.UnknownOutcome;
    }

    private static StorageFailureKind? TryClassify(Exception exception)
    {
        if (exception is InconsistentStateException)
        {
            return StorageFailureKind.DidNotPersist;
        }

        if (exception is RequestFailedException requestFailed)
        {
            return ClassifyRequestFailed(requestFailed);
        }

        if (exception is AggregateException aggregateException)
        {
            var hasDidNotPersist = false;
            foreach (var inner in aggregateException.Flatten().InnerExceptions)
            {
                var innerKind = TryClassify(inner);
                if (innerKind is StorageFailureKind.UnknownOutcome)
                {
                    return StorageFailureKind.UnknownOutcome;
                }

                hasDidNotPersist |= innerKind is StorageFailureKind.DidNotPersist;
            }

            return hasDidNotPersist ? StorageFailureKind.DidNotPersist : null;
        }

        return exception.InnerException is null ? null : TryClassify(exception.InnerException);
    }

    private static StorageFailureKind ClassifyRequestFailed(RequestFailedException exception)
    {
        if (IsAmbiguousOrTransient(exception))
        {
            return StorageFailureKind.UnknownOutcome;
        }

        return IsRejectedBeforePersistence(exception)
            ? StorageFailureKind.DidNotPersist
            : StorageFailureKind.UnknownOutcome;
    }

    private static bool IsAmbiguousOrTransient(RequestFailedException exception)
    {
        return exception.Status is 0
            or (int)HttpStatusCode.RequestTimeout
            or 429
            or >= 500
            || IsErrorCode(
                exception,
                BlobErrorCode.OperationTimedOut.ToString(),
                BlobErrorCode.ServerBusy.ToString(),
                BlobErrorCode.InternalError.ToString(),
                TableErrorCode.OperationTimedOut.ToString(),
                "ServerBusy",
                "InternalError",
                "AccountIOPSLimitExceeded");
    }

    private static bool IsRejectedBeforePersistence(RequestFailedException exception)
    {
        return exception.Status is >= 400 and < 500
            || IsErrorCode(
                exception,
                BlobErrorCode.ConditionNotMet.ToString(),
                BlobErrorCode.AppendPositionConditionNotMet.ToString(),
                BlobErrorCode.MaxBlobSizeConditionNotMet.ToString(),
                BlobErrorCode.SequenceNumberConditionNotMet.ToString(),
                BlobErrorCode.SourceConditionNotMet.ToString(),
                BlobErrorCode.TargetConditionNotMet.ToString(),
                BlobErrorCode.BlobAlreadyExists.ToString(),
                BlobErrorCode.BlobNotFound.ToString(),
                BlobErrorCode.ContainerNotFound.ToString(),
                BlobErrorCode.ResourceAlreadyExists.ToString(),
                BlobErrorCode.ResourceNotFound.ToString(),
                TableErrorCode.UpdateConditionNotSatisfied.ToString(),
                TableErrorCode.EntityAlreadyExists.ToString(),
                TableErrorCode.EntityNotFound.ToString(),
                TableErrorCode.ResourceNotFound.ToString(),
                TableErrorCode.TableNotFound.ToString());
    }

    private static bool IsErrorCode(RequestFailedException exception, params string[] errorCodes)
    {
        return !string.IsNullOrWhiteSpace(exception.ErrorCode)
            && errorCodes.Contains(exception.ErrorCode, StringComparer.Ordinal);
    }
}