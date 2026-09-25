using Azure;
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
    public AzureStorageStateManager(
        IPersistentState<T> storage,
        Func<T> createInitialState,
        Action<T>? configureState = null)
        : base(storage, createInitialState, configureState)
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
public sealed class AzureStorageStateManagerFactory : IStateManagerFactory
{
    /// <inheritdoc/>
    public IStateManager<T> Create<T>(IPersistentState<T> storage, Func<T> createInitialState, Action<T>? configureState = null)
        where T : class, IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new AzureStorageStateManager<T>(storage, createInitialState, configureState);
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
            // Orleans' Azure providers wrap ETag mismatches, HTTP 412, and existence
            // errors on conditional write/clear as InconsistentStateException. The
            // rejection proves this attempt did not persist, but it also proves the
            // local ETag is stale, so recovery must re-read before rethrowing.
            return StorageFailureKind.Conflict;
        }

        if (exception is RequestFailedException requestFailed)
        {
            return ClassifyRequestFailed(requestFailed);
        }

        if (exception is AggregateException aggregateException)
        {
            var kind = default(StorageFailureKind?);
            foreach (var inner in aggregateException.Flatten().InnerExceptions)
            {
                var innerKind = TryClassify(inner);
                // An unclassified inner failure (for example a transport timeout) is
                // also uncertain. A rejected retry cannot prove an earlier attempt failed.
                if (innerKind is null or StorageFailureKind.UnknownOutcome)
                {
                    return StorageFailureKind.UnknownOutcome;
                }

                // Precedence within an aggregate: Conflict wins over DidNotPersist, because
                // a Conflict inner means an ETag mismatch was observed and the local
                // baseline must be refreshed.
                if (kind is null || innerKind is StorageFailureKind.Conflict)
                {
                    kind = innerKind;
                }
            }

            return kind;
        }

        return exception.InnerException is null ? null : TryClassify(exception.InnerException);
    }

    private static StorageFailureKind ClassifyRequestFailed(RequestFailedException exception)
    {
        if (IsAmbiguousOrTransient(exception))
        {
            return StorageFailureKind.UnknownOutcome;
        }

        if (IsConflict(exception))
        {
            return StorageFailureKind.Conflict;
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
            || IsAmbiguousErrorCode(exception.ErrorCode);
    }

    private static bool IsConflict(RequestFailedException exception)
    {
        return exception.Status is (int)HttpStatusCode.PreconditionFailed
            or (int)HttpStatusCode.Conflict
            or (int)HttpStatusCode.NotFound
            || IsConflictErrorCode(exception.ErrorCode);
    }

    private static bool IsRejectedBeforePersistence(RequestFailedException exception)
    {
        return exception.Status is >= 400 and < 500
            || IsRejectedErrorCode(exception.ErrorCode);
    }

    private static bool IsAmbiguousErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "OperationTimedOut" => true,
            "ServerBusy" => true,
            "InternalError" => true,
            "AccountIOPSLimitExceeded" => true,
            _ => false
        };
    }

    private static bool IsConflictErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "ConditionNotMet" => true,
            "UpdateConditionNotSatisfied" => true,
            "BlobAlreadyExists" => true,
            "EntityAlreadyExists" => true,
            "ResourceAlreadyExists" => true,
            "BlobNotFound" => true,
            "EntityNotFound" => true,
            "ResourceNotFound" => true,
            _ => false
        };
    }

    private static bool IsRejectedErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "AppendPositionConditionNotMet" => true,
            "MaxBlobSizeConditionNotMet" => true,
            "SequenceNumberConditionNotMet" => true,
            "SourceConditionNotMet" => true,
            "TargetConditionNotMet" => true,
            "ContainerNotFound" => true,
            "TableNotFound" => true,
            _ => false
        };
    }
}
