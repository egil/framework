using Orleans.Storage;

namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Base implementation of <see cref="IStateManager{T}"/>. Wraps an
/// <see cref="IPersistentState{T}"/> and provides committed-state fencing,
/// version stamping for <see cref="VersionedState"/>-derived types, and
/// configurable write-failure recovery behavior.
/// </summary>
/// <remarks>
/// <para>
/// <b>Committed-state fence:</b> <see cref="State"/> returns the last
/// committed value, or the configured default when no record exists. During
/// <see cref="WriteAsync"/>, the underlying
/// <see cref="IPersistentState{T}"/>.State is mutated, but the caller's view
/// is updated only after the write succeeds. On failure, the recovery path
/// re-reads from storage to determine whether the write actually landed.
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
///   <description>Write genuinely failed. Persisted state adopted and original
///   exception rethrown.</description>
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
    private readonly Func<T> createInitialState;
    private readonly Action<T>? configureState;
    private T state;

    /// <summary>
    /// Initializes the manager over the grain's persistent state facet,
    /// adopting the currently loaded <c>IPersistentState&lt;T&gt;.State</c>
    /// as the committed snapshot.
    /// </summary>
    protected StateManagerBase(
        IPersistentState<T> storage,
        Func<T> createInitialState,
        Action<T>? configureState = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(createInitialState);
        this.storage = storage;
        this.createInitialState = createInitialState;
        this.configureState = configureState;
        state = ResolveLoadedState();
        storage.State = state;
        configureState?.Invoke(state);
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
        Adopt(ResolveLoadedState());
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
        }
        catch (Exception ex)
        {
            var failureKind = ClassifyWriteFailure(ex);
            if (failureKind is StorageFailureKind.DidNotPersist)
            {
                // Provider-specific classification says the write never reached durable storage.
                // Revert our local fence immediately and rethrow the original write error.
                RestoreState(previousState);
                throw;
            }

            if (!await TryReadForRecoveryAsync(previousState))
            {
                throw;
            }

            // The read succeeded. Validation and user configuration are not storage
            // failures: let them propagate instead of hiding them behind the write error.
            T? persisted = storage.State;
            Adopt(ResolveLoadedState());
            if (ex is not InconsistentStateException && storage.RecordExists && IsEquivalent(persisted, newState))
            {
                return;
            }

            // A mismatch proves the write did not land. A concurrency conflict
            // must be surfaced even if values happen to match. Once read-back
            // succeeds, keep the server value paired with its refreshed ETag.
            throw;
        }

        // The write is complete. A callback failure must not trigger another storage
        // read or be mistaken for a lost response and silently retried.
        Adopt(storage.State);
    }

    /// <inheritdoc/>
    public async Task ClearAsync()
    {
        var previousState = state;
        try
        {
            await storage.ClearStateAsync();
        }
        catch (Exception ex)
        {
            if (ClassifyClearFailure(ex) is StorageFailureKind.DidNotPersist)
            {
                RestoreState(previousState);
                throw;
            }

            if (!await TryReadForRecoveryAsync(previousState))
            {
                throw;
            }

            // Once storage has returned a record (or confirmed its absence), state
            // validation and configuration failures belong to the caller, not recovery.
            Adopt(ResolveLoadedState());
            if (ex is not InconsistentStateException && !storage.RecordExists)
            {
                return;
            }

            throw;
        }

        // Clearing storage and constructing/configuring its replacement are distinct
        // operations. A bad factory or callback cannot turn a successful clear into
        // an ambiguous storage failure or cause configuration to be retried.
        Adopt(CreateInitialState());
    }

    private async Task<bool> TryReadForRecoveryAsync(T previousState)
    {
        try
        {
            await storage.ReadStateAsync();
            return true;
        }
        catch
        {
            // Only a failed storage read leaves durability unknown. Restore the local
            // snapshot and let the caller rethrow the original write/clear exception.
            RestoreState(previousState);
            return false;
        }
    }

    /// <summary>
    /// Classifies a write failure so the base can decide recovery behavior.
    /// </summary>
    protected abstract StorageFailureKind ClassifyWriteFailure(Exception exception);

    /// <summary>
    /// Classifies a clear failure so the base can decide recovery behavior.
    /// </summary>
    protected virtual StorageFailureKind ClassifyClearFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return StorageFailureKind.UnknownOutcome;
    }

    /// <summary>
    /// Decides whether a read-back state proves an ambiguous write actually
    /// landed (the "lost response" case in <see cref="WriteAsync"/>).
    /// </summary>
    /// <remarks>
    /// For non-<see cref="VersionedState"/> types this delegates to the state
    /// type's own equality, which therefore carries recovery semantics: it
    /// must never return <c>true</c> when the persisted state is missing data
    /// the attempted write contained, or recovery would adopt a foreign state
    /// and silently lose that data. <c>Outbox&lt;T&gt;.Equals</c> documents
    /// how its persisted revision satisfies this contract in O(1).
    /// </remarks>
    private static bool IsEquivalent(T? persisted, T attempted)
    {
        if (persisted is null)
        {
            return false;
        }

        if (persisted is VersionedState persistedVersioned
            && attempted is VersionedState attemptedVersioned)
        {
            return persistedVersioned.Version == attemptedVersioned.Version;
        }

        // Outbox equality uses its persisted snapshot revision. Matching sequence windows
        // alone cannot prove persistence when competing appends share a timestamp. Other
        // non-versioned state types must provide an equally reliable recovery comparison.
        return persisted.Equals(attempted);
    }

    private T ResolveLoadedState() => storage.RecordExists
        ? storage.State ?? throw new InvalidOperationException("An existing state record contained null.")
        : CreateInitialState();

    private T CreateInitialState() => createInitialState()
        ?? throw new InvalidOperationException("The initial state factory returned null.");

    private void Adopt(T value)
    {
        // Publish the new snapshot before invoking caller code: a callback may read
        // the manager or raw facet as well as its argument. If configuration fails,
        // keep the adopted durable snapshot visible and report the callback failure.
        storage.State = value;
        state = value;
        configureState?.Invoke(value);
    }

    private void RestoreState(T previousState)
    {
        state = previousState;

        storage.State = previousState;
    }
}
