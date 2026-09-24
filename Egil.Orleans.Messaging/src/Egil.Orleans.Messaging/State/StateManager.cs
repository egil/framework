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
/// committed value, the configured default when no record exists, or a value
/// deliberately published by <see cref="State"/>. It never returns an
/// in-flight write candidate: during <see cref="WriteAsync(T, CancellationToken)"/>
/// the underlying <see cref="IPersistentState{T}"/>.State is mutated, but the
/// caller's view is updated only after the write succeeds. On failure, the
/// recovery path re-reads from storage to determine whether the write actually
/// landed. An unsaved value is not a write candidate — its durability is knowingly
/// deferred rather than unknown — which is why it is exposed and flagged instead
/// of fenced. See the deferred-writes note below.
/// </para>
/// <para>
/// <b>Version stamping:</b> If <typeparamref name="T"/> derives from
/// <see cref="VersionedState"/>, the manager stamps a fresh
/// <see cref="Guid.CreateVersion7()"/> on a copy before every write. The
/// caller's record remains unchanged. The recovery path then compares versions
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
///   <term>UnknownOutcome, re-read succeeds, state matches</term>
///   <description>Lost-response — write landed. Exception swallowed unless it is an InconsistentStateException.</description>
/// </item>
/// <item>
///   <term>UnknownOutcome, re-read succeeds, state does not match</term>
///   <description>Write genuinely failed. Persisted state adopted and original
///   exception rethrown.</description>
/// </item>
/// <item>
///   <term>Conflict, re-read succeeds</term>
///   <description>Persisted state and refreshed ETag adopted. Original exception
///   always rethrown, even when the state matches the attempted write.</description>
/// </item>
/// <item>
///   <term>DidNotPersist</term>
///   <description>No recovery read. State reverts to the last stored snapshot and
///   the original exception is rethrown.</description>
/// </item>
/// <item>
///   <term>UnknownOutcome or Conflict, re-read also throws</term>
///   <description>Double failure. <c>State</c> reverts to the last stored snapshot.
///   Original exception rethrown. Caller must call <see cref="ReadAsync"/>
///   before the next write to re-sync.</description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Deferred writes:</b> the manager tracks two snapshots. <see cref="State"/> is what the
/// grain sees and may hold a value published by <see cref="State"/>; alongside it
/// the manager keeps the last value known to correspond to storage, which is what
/// every recovery path above reverts to. Keeping them apart is what lets
/// <see cref="State"/> be called repeatedly with no intervening write without the
/// recovery baseline drifting onto a value storage never accepted. With nothing
/// unsaved the two are the same reference, so every path behaves exactly as it does
/// without deferred writes.
/// </para>
/// <para>
/// <b>Concurrency:</b> Uses the storage provider's optimistic concurrency
/// checks (typically ETag-based). Writes against stale versions fail with
/// <c>InconsistentStateException</c>.
/// </para>
/// <para>
/// <b>Thread safety:</b> Relies on Orleans turn-based concurrency. In a
/// <c>[Reentrant]</c> grain, do not let two storage mutations —
/// <see cref="WriteAsync(T, CancellationToken)"/>, <see cref="ClearAsync"/> or
/// <see cref="ReadAsync"/> — overlap; they share the storage facet and its ETag,
/// and the manager does not serialize them.
/// <see cref="State"/> is the exception, because it touches no storage: a
/// stage that interleaves an in-flight write has a defined outcome, which is that
/// the write adopts the value it wrote and the stage is discarded. That costs a
/// redelivery for an outbox acknowledgement, never a lost write. Such a stage also
/// leaves the storage facet alone until the operation finishes, because the provider
/// may not have serialized it yet. Interleaved <em>reads</em> of <see cref="State"/>
/// are always safe.
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
    private T lastStored;
    private bool hasUnsavedChanges;
    private bool operationInProgress;
    private StateManagerHooks<T> hooks = new();
    private readonly StateManagerHookDispatcher<T> hookDispatcher = new();
    private bool initialized;

    /// <inheritdoc/>
    public void ConfigureHooks(Action<StateManagerHooks<T>> configure)
    {
        Volatile.Write(ref hooks, StateManagerHooks<T>.Create(configure));
    }

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideHandler();
        if (initialized)
        {
            return Task.CompletedTask;
        }

        // A failed notification is still an attempted initial notification; calling
        // this again must not replay side effects which may already have completed.
        initialized = true;
        return hookDispatcher.InvokeAsync(Volatile.Read(ref hooks), state, StateManagerOperation.Read,
            storage.RecordExists, cancellationToken);
    }

    private void ThrowIfInsideHandler()
    {
        if (hookDispatcher.IsInvoking)
        {
            throw new InvalidOperationException("Storage operations on this manager cannot be called from a lifecycle handler.");
        }
    }

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
        ThrowIfFacetCannotBeReadBack(storage);
        this.storage = storage;
        this.createInitialState = createInitialState;
        this.configureState = configureState;
        state = ResolveLoadedState();
        lastStored = state;
        storage.State = state;
        configureState?.Invoke(state);
    }

    /// <inheritdoc/>
    public T State
    {
        get => state;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            // Assignment is Adopt without the durability claim: the snapshot moves forward
            // but lastStored keeps pointing at what storage holds, so repeated assignment
            // never drifts the value a failed write falls back to.
            //
            // The facet is left alone while an operation is in progress. It holds the
            // GrainState the provider was handed and a provider may serialize it after its
            // first await, so replacing the value there would persist this snapshot instead
            // of the one being written. It is also what ResolveLoadedState reads back, so an
            // unsaved value left there would be adopted as if storage had returned it. The
            // operation's own completion path re-establishes the facet either way, and an
            // assignment that raced it is discarded. Only reachable under reentrancy or
            // interleaved acknowledgement callbacks.
            hasUnsavedChanges = true;
            Publish(value, mirrorToFacet: !operationInProgress);
        }
    }

    /// <inheritdoc/>
    public bool HasUnsavedChanges => hasUnsavedChanges;

    /// <inheritdoc/>
    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideHandler();
        var operationHooks = Volatile.Read(ref hooks);
        cancellationToken.ThrowIfCancellationRequested();
    // The guard spans the storage call and the adoption that follows it, not just
    // the await. Adoption reads the facet back — ResolveLoadedState and the recovery
    // comparison both do — so an assignment that slipped in between the two would be mistaken
    // for what storage returned. Only assignment observes this, and only under reentrancy or
    // interleaved acknowledgement callbacks.
        operationInProgress = true;
        try
        {
            await storage.ReadStateAsync(cancellationToken);
            AdoptLoadedState();
            await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Read,
                storage.RecordExists, cancellationToken);
        }
        finally
        {
            operationInProgress = false;
        }
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideHandler();
        // Deliberately does not observe the token when there is nothing to write: this
        // method exists to be called unconditionally from deactivation hooks, where the
        // token is routinely already canceled.
        return hasUnsavedChanges ? WriteAsync(state, cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(T newState, CancellationToken cancellationToken = default)
    {
        ThrowIfInsideHandler();
        var operationHooks = Volatile.Read(ref hooks);
        ArgumentNullException.ThrowIfNull(newState);
        cancellationToken.ThrowIfCancellationRequested();

        if (newState is VersionedState versioned)
        {
            // Record cloning preserves the concrete state type. This manager also accepts
            // non-versioned T, so its generic constraint cannot express that invariant.
            newState = (T)(object)(versioned with { Version = Guid.CreateVersion7() });
        }

        // The guard spans the storage call and the adoption that follows it, not just
        // the await. Adoption reads the facet back — ResolveLoadedState and the recovery
        // comparison both do — so an assignment that slipped in between the two would be mistaken
        // for what storage returned. Only assignment observes this, and only under reentrancy or
        // interleaved acknowledgement callbacks.
        operationInProgress = true;
        try
        {
            await WriteCoreAsync(newState, operationHooks, cancellationToken);
        }
        finally
        {
            operationInProgress = false;
        }
    }

    private async Task WriteCoreAsync(T newState, StateManagerHooks<T> operationHooks, CancellationToken cancellationToken)
    {
        storage.State = newState;

        try
        {
            await storage.WriteStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var failureKind = ClassifyWriteFailure(ex);
            if (failureKind is StorageFailureKind.DidNotPersist)
            {
                // Provider-specific classification says the write never reached durable storage
                // and the stored version was not contradicted. Revert our local fence
                // immediately and rethrow the original write error.
                RestoreState();
                throw;
            }

            if (!await TryReadForRecoveryAsync(cancellationToken))
            {
                throw;
            }

            // The read succeeded. Validation and user configuration are not storage
            // failures: let them propagate instead of hiding them behind the write error.
            T? persisted = storage.State;
            AdoptLoadedState();
            if (failureKind is not StorageFailureKind.Conflict
                && ex is not InconsistentStateException
                && storage.RecordExists
                && IsEquivalent(persisted, newState))
            {
                await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Write, true, cancellationToken);
                return;
            }

            // A mismatch proves the write did not land. A concurrency conflict
            // must be surfaced even if values happen to match. Once read-back
            // succeeds, keep the server value paired with its refreshed ETag.
            await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Read,
                storage.RecordExists, cancellationToken, ex);
            throw;
        }

        // The write is complete, so newState is what storage holds. Adopting it rather
        // than re-reading the facet matters when an assignment interleaved with the await:
        // the facet would then hold the unsaved value, and adopting that would mark a
        // value storage never saw as durable and lose this write's fence.
        // A callback failure must not trigger another storage read or be mistaken for
        // a lost response and silently retried.
        Adopt(newState);
        await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Write, true, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInsideHandler();
        var operationHooks = Volatile.Read(ref hooks);
        cancellationToken.ThrowIfCancellationRequested();
        operationInProgress = true;
        try
        {
            await ClearCoreAsync(operationHooks, cancellationToken);
        }
        finally
        {
            operationInProgress = false;
        }
    }

    private async Task ClearCoreAsync(StateManagerHooks<T> operationHooks, CancellationToken cancellationToken)
    {
        try
        {
            await storage.ClearStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var failureKind = ClassifyClearFailure(ex);
            if (failureKind is StorageFailureKind.DidNotPersist)
            {
                RestoreState();
                throw;
            }

            if (!await TryReadForRecoveryAsync(cancellationToken))
            {
                throw;
            }

            // Once storage has returned a record (or confirmed its absence), state
            // validation and configuration failures belong to the caller, not recovery.
            AdoptLoadedState();
            if (failureKind is not StorageFailureKind.Conflict
                && ex is not InconsistentStateException
                && !storage.RecordExists)
            {
                await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Clear, false, cancellationToken);
                return;
            }

            await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Read,
                storage.RecordExists, cancellationToken, ex);
            throw;
        }

        // Clearing storage and constructing/configuring its replacement are distinct
        // operations. A bad factory or callback cannot turn a successful clear into
        // an ambiguous storage failure or cause configuration to be retried.
        // The clear intentionally happens first: factory failure does not undo the
        // requested deletion. A valid, non-null default is the caller's contract;
        // if it is violated, no usable-state guarantee applies to this manager.
        // In particular, its previous snapshot must not be treated as persisted data.
        // The unsaved question is settled first, before the factory can throw: a
        // deactivation flush must never write an unsaved value back over a record the
        // grain just asked to delete.
        hasUnsavedChanges = false;
        Adopt(CreateInitialState());
        await hookDispatcher.InvokeAsync(operationHooks, state, StateManagerOperation.Clear, false, cancellationToken);
    }

    private async Task<bool> TryReadForRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Cancellation cannot prove whether the mutation landed. If recovery
            // is canceled too, retain the previous fence and the original failure.
            cancellationToken.ThrowIfCancellationRequested();
            await storage.ReadStateAsync(cancellationToken);
            return true;
        }
        catch
        {
            // Only a failed storage read leaves durability unknown. Restore the local
            // snapshot and let the caller rethrow the original write/clear exception.
            RestoreState();
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
    /// landed (the "lost response" case in <see cref="WriteAsync(T, CancellationToken)"/>).
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

    private static void ThrowIfFacetCannotBeReadBack(IPersistentState<T> storage)
    {
        // Orleans.Journaling's DurableState<T> is an IPersistentState<T> whose
        // ReadStateAsync is a no-op: a journal is replayed at activation rather than
        // re-read on demand. This manager resolves an ambiguous write by reading the
        // record back, so against a journaled facet it would compare the attempted value
        // with itself, find them equal, and report a failed write as a successful one.
        // No configuration makes that combination safe, so refuse it at construction.
        //
        // Matched by interface name rather than by type: IJournaledState is public in
        // that package while DurableState<T> is internal, and this library does not
        // reference it. A rename upstream costs us the detection, never a false positive.
        foreach (var contract in storage.GetType().GetInterfaces())
        {
            if (!string.Equals(contract.FullName, "Orleans.Journaling.IJournaledState", StringComparison.Ordinal))
            {
                continue;
            }

            throw new NotSupportedException(
                $"'{storage.GetType().FullName}' is a journaled state. Its ReadStateAsync does not re-read " +
                $"storage, and {nameof(IStateManager<T>)}<T> resolves an ambiguous write by reading the record " +
                "back, so it would report a failed write as a successful one. Use the journal's own durability " +
                "rather than wrapping it, or supply a facet backed by an IGrainStorage provider.");
        }
    }

    private void AdoptLoadedState()
    {
        // Storage has answered, so the unsaved question is settled whatever resolving the
        // value does next. Clearing first means an invalid record or a throwing default
        // factory cannot leave a stale unsaved value behind for a later flush to write
        // back, on top of the state it just read.
        hasUnsavedChanges = false;

        T loaded;
        try
        {
            loaded = ResolveLoadedState();
        }
        catch
        {
            // The answer could not be turned into a usable state. Fall back to the last
            // stored value rather than leaving an unsaved one visible: with the marker
            // already cleared it would be indistinguishable from durable state, which is
            // the one thing worse than reporting the failure.
            RestoreState();
            throw;
        }

        Adopt(loaded);
    }

    private T ResolveLoadedState() => storage.RecordExists
        ? storage.State ?? throw new InvalidOperationException("An existing state record contained null.")
        : CreateInitialState();

    private T CreateInitialState() => createInitialState()
        ?? throw new InvalidOperationException("The initial state factory returned null.");

    private void Adopt(T value)
    {
        // The value is known to match storage, so it becomes the baseline recovery falls
        // back to and settles the durability question for anything assigned before it.
        lastStored = value;
        hasUnsavedChanges = false;
        Publish(value, mirrorToFacet: true);
    }

    private void Publish(T value, bool mirrorToFacet)
    {
        // Publish the new snapshot before invoking caller code: a callback may read
        // the manager or raw facet as well as its argument. If configuration fails,
        // keep the published snapshot visible and report the callback failure.
        if (mirrorToFacet)
        {
            storage.State = value;
        }

        state = value;
        configureState?.Invoke(value);
    }

    private void RestoreState()
    {
        // A resolved failure discards unsaved work rather than preserving it: the fence
        // must fall back to a value storage actually holds. For an outbox that costs a
        // redelivery of the already-delivered batch, which the next post run corrects.
        state = lastStored;
        storage.State = lastStored;
        hasUnsavedChanges = false;
    }
}
