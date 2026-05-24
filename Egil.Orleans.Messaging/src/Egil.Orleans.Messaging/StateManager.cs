using Orleans.Storage;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Classifies a storage write failure for recovery decisions.
/// </summary>
public enum WriteFailureKind
{
    /// <summary>
    /// Unknown outcome. The write may have persisted. Run read-back recovery.
    /// </summary>
    UnknownOutcome,

    /// <summary>
    /// The write definitely did not persist. Skip read-back and rethrow.
    /// </summary>
    DidNotPersist
}

/// <summary>
/// Base implementation of <see cref="IStateManager{T}"/>. Wraps an
/// <see cref="IPersistentState{T}"/> and provides committed-state fencing,
/// version stamping for <see cref="VersionedState"/>-derived types, and
/// configurable write-failure recovery behavior.
/// </summary>
/// <remarks>
/// <para>
/// <b>Committed-state fence:</b> <see cref="State"/> always returns the
/// last successfully written value. During <see cref="WriteAsync"/>, the
/// underlying <see cref="IPersistentState{T}"/>.State is mutated, but the
/// caller's view is updated only after the write succeeds. On failure, the
/// recovery path re-reads from storage to determine whether the write
/// actually landed.
/// </para>
/// <para>
/// <b>Version stamping:</b> If <typeparamref name="T"/> derives from
/// <see cref="VersionedState"/>, the manager stamps a fresh
/// <see cref="Guid.CreateVersion7()"/> on the <see cref="VersionedState.Version"/>
/// property before every write. The recovery path then compares versions
/// directly (pattern-matched via <c>is VersionedState</c>) instead of
/// relying on <see cref="IEquatable{T}.Equals(T)"/>, which avoids the
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// reference-equality trap.
/// </para>
/// <para>
/// <b>Recovery matrix:</b>
/// <list type="table">
/// <listheader>
///   <term>Failure</term>
///   <description>Outcome after <c>WriteAsync</c></description>
/// </listheader>
/// <item>
///   <term>Success</term>
///   <description><c>State == newState</c>, returns normally.</description>
/// </item>
/// <item>
///   <term>Write throws, re-read succeeds, state matches</term>
///   <description>Lost-response — write landed. Exception swallowed.</description>
/// </item>
/// <item>
///   <term>Write throws, re-read succeeds, state does not match</term>
///   <description>Write genuinely failed. Original exception rethrown.</description>
/// </item>
/// <item>
///   <term>Write throws, re-read also throws</term>
///   <description>Double failure. <c>State</c> reverts to pre-write snapshot.
///   Original exception rethrown. Caller must call <see cref="ReadAsync"/>
///   before the next write to re-sync.</description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Concurrency:</b> Uses the storage provider's optimistic concurrency
/// checks (typically ETag-based). Writes against stale versions fail with
/// <c>InconsistentStateException</c>.
/// </para>
/// <para>
/// <b>Thread safety:</b> Relies on Orleans turn-based concurrency. Not safe
/// for <c>[Reentrant]</c> grains unless <see cref="State"/> is only read
/// (never written) from interleaved calls.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// <inheritdoc cref="IStateManager{T}" path="/typeparam"/>
/// </typeparam>
public abstract class StateManagerBase<T> : IStateManager<T>
    where T : class, IEquatable<T>
{
    private readonly IPersistentState<T> storage;
    private T state;

    protected StateManagerBase(IPersistentState<T> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        this.storage = storage;
        state = storage.State;
    }

    /// <inheritdoc/>
    public T State
    {
        get => state;
    }

    /// <inheritdoc/>
    public async Task ReadAsync()
    {
        await storage.ReadStateAsync();
        state = storage.State;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(T newState)
    {
        ArgumentNullException.ThrowIfNull(newState);

        var previousState = state;

        if (newState is VersionedState versioned)
        {
            versioned.Version = Guid.CreateVersion7();
        }

        storage.State = newState;

        try
        {
            await storage.WriteStateAsync();
            state = storage.State;
            return;
        }
        catch (Exception ex)
        {
            var failureKind = ClassifyWriteFailure(ex);
            if (failureKind is WriteFailureKind.DidNotPersist)
            {
                // Provider-specific classification says the write never reached durable storage.
                // Revert our local fence immediately and rethrow the original write error.
                state = previousState;
                storage.State = previousState;
                throw;
            }

            try
            {
                // Unknown outcome: write may have persisted despite the exception.
                // Read back from storage to distinguish "lost response" from "real failure."
                await storage.ReadStateAsync();
                var persisted = storage.State;

                if (ex is InconsistentStateException)
                {
                    // Concurrency conflicts must be surfaced even if values happen to match.
                    // A coincidental equality must not hide an optimistic concurrency violation.
                    storage.State = previousState;
                    throw;
                }

                if (IsEquivalent(persisted, newState))
                {
                    // Lost-response case: write landed, but acknowledgement failed.
                    // Advance committed fence to persisted value and swallow write exception.
                    state = persisted;
                    return;
                }

                // Read-back proved write did not land; restore local snapshot and rethrow.
                storage.State = previousState;
                throw;
            }
            catch when (ex is not InconsistentStateException)
            {
                // no-op; throw original below
            }

            // Write failed and recovery read also failed: keep committed fence coherent by
            // reverting to the pre-write snapshot, then surface the original write error.
            state = previousState;
            storage.State = previousState;
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ClearAsync()
    {
        var previousState = state;
        var recoveryReadSucceeded = false;

        try
        {
            await storage.ClearStateAsync();
            state = storage.State;
            return;
        }
        catch
        {
            try
            {
                await storage.ReadStateAsync();
                recoveryReadSucceeded = true;

                if (!storage.RecordExists)
                {
                    state = storage.State;
                    return;
                }

                state = storage.State;
            }
            catch
            {
                // no-op; rethrow original clear error below
            }

            if (!recoveryReadSucceeded)
            {
                state = previousState;
                storage.State = previousState;
            }

            throw;
        }
    }

    /// <summary>
    /// Classifies a write failure so the base can decide recovery behavior.
    /// </summary>
    protected abstract WriteFailureKind ClassifyWriteFailure(Exception exception);

    private static bool IsEquivalent(T persisted, T attempted)
    {
        if (persisted is VersionedState persistedVersioned
            && attempted is VersionedState attemptedVersioned)
        {
            return persistedVersioned.Version == attemptedVersioned.Version;
        }

        return persisted.Equals(attempted);
    }
}

/// <summary>
/// Default <see cref="IStateManager{T}"/> implementation for general storage providers.
/// </summary>
public sealed class DefaultStateManager<T> : StateManagerBase<T>
    where T : class, IEquatable<T>
{
    public DefaultStateManager(IPersistentState<T> storage)
        : base(storage)
    {
    }

    /// <inheritdoc/>
    protected override WriteFailureKind ClassifyWriteFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return WriteFailureKind.UnknownOutcome;
    }
}
