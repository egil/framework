namespace Egil.Orleans.Messaging.State;

/// <summary>
/// A thin wrapper around <see cref="IPersistentState{TState}"/> that guarantees
/// the grain's observable <see cref="State"/> never exposes a value whose durability
/// is unknown, even when <see cref="WriteAsync(T, CancellationToken)"/> fails ambiguously
/// (timeout, network drop, server 5xx, ETag conflict). It exposes the loaded or
/// committed snapshot, a default for absent storage, or a value the grain deliberately
/// published with <see cref="State"/> and has not written yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Committed-state fence:</b> <see cref="State"/> exposes the loaded or last
/// successfully written value, or a configured default for a missing record. During an in-flight write, the underlying
/// <see cref="IPersistentState{TState}"/>.State already holds the uncommitted
/// value. Methods marked <c>[AlwaysInterleave]</c> that read <see cref="State"/>
/// through this interface are guaranteed to never observe a write candidate,
/// meaning a value whose durability is unknown because a write is still running.
/// This is the primary reason <see cref="IStateManager{T}"/> exists as a wrapper
/// rather than extension methods on <see cref="IPersistentState{TState}"/>.
/// </para>
/// <para>
/// <b>Deferred writes:</b> assigning <see cref="State"/> narrows that fence
/// deliberately. An assigned value's durability is knowingly <em>deferred</em> rather
/// than unknown, so <see cref="State"/> does expose it and
/// <see cref="HasUnsavedChanges"/> reports it. That lets a grain fold several changes
/// into a single storage write, at the cost that the value is lost if the activation
/// ends without writing it.
/// </para>
/// <para>
/// <b>Cancellation:</b> Operations forward the token to storage, including recovery
/// reads. Cancellation is cooperative and depends on provider support. An already
/// canceled token prevents storage access and write-version stamping. Once a write
/// or clear starts, cancellation does not prove that it failed to persist. If recovery
/// is canceled after the operation started, <see cref="State"/> reverts to the last
/// stored value and the original operation exception is rethrown. Call <see cref="ReadAsync"/> with a fresh token before the
/// next mutation to refresh state and ETag. A provider-confirmed success is adopted
/// even if cancellation was requested concurrently.
/// </para>
/// <para>
/// <b>Usage:</b> Inject the manager on the <c>[PersistentState]</c> parameter itself:
/// <code>
/// public MyGrain([PersistentState("state", "Default")] IStateManager&lt;MyState&gt; state)
/// </code>
/// The facet is built underneath, so the state hydrates before <c>OnActivateAsync</c>
/// and takes part in grain migration as usual. Express a non-trivial default and any
/// transient dependencies on the state type, with <see cref="IStateDefault{TSelf}"/>
/// and <see cref="IConfigurableState"/>.
/// </para>
/// <para>
/// Alternatively, inject <see cref="IPersistentState{TState}"/> and register the
/// manager in the grain constructor, which is what a grain needs when its default or
/// its configuration closes over something only the constructor has:
/// <code>
/// stateManager = this.RegisterStateManager("state", storage);
/// </code>
/// The raw <see cref="IPersistentState{TState}"/> should not be accessed
/// directly after wrapping — doing so bypasses the committed-state fence.
/// </para>
/// <para>
/// <b>Recovery:</b> On ambiguous write failure, the manager re-reads from
/// storage. If the write actually persisted (detected via version or equality
/// check), it swallows the exception. If the write did not persist, it rethrows.
/// If both write and re-read fail (double failure), the manager reverts to the
/// last stored state and rethrows — the grain must call <see cref="ReadAsync"/>
/// before its next write to refresh the ETag. "Last stored" rather than "previous"
/// matters once <see cref="State"/> is in play: reverting has to land on a value
/// storage actually holds, so an unsaved value is discarded rather than restored.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The grain state type. Must be a reference type (atomic pointer swap for
/// interleaved reads) and implement <see cref="IEquatable{T}"/> (recovery path
/// compares server-side state to attempted write). Records satisfy both for free.
/// For state containing <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// or other types with reference-based equality, inherit from
/// <see cref="VersionedState"/> — the recovery path pattern-matches against it
/// and compares <see cref="VersionedState.Version"/> directly, bypassing
/// <c>Equals</c> entirely.
/// <para>
/// <b>Deep immutability:</b> Every type referenced from the state record
/// should also be immutable. The strength of this requirement depends on
/// how the grain is used:
/// </para>
/// <para>
/// <b>Required</b> when the grain uses <c>[AlwaysInterleave]</c> on any
/// read method. Interleaved readers access <see cref="State"/> concurrently
/// with an in-flight command building the next state value. If the state
/// graph contains mutable reference types, a reader could observe a
/// partially mutated object even though the <em>root</em> reference hasn't
/// been swapped yet (the old state's inner mutable object is being modified
/// in place by the command).
/// </para>
/// <para>
/// <b>Strongly recommended</b> even without interleaving. Orleans default
/// turn-based concurrency prevents concurrent access, so torn reads cannot
/// occur. However, immutable state still provides value: it makes the
/// functional-command pattern (<c>with { ... }</c>) predictable, prevents
/// accidental mutation of the "previous" snapshot held by the recovery
/// path, and avoids subtle bugs if <c>[AlwaysInterleave]</c> is added later.
/// </para>
/// <para>
/// Use <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableDictionary&lt;K,V&gt;</c>,
/// records, and value objects throughout. Mutable collections
/// (<see cref="System.Collections.Generic.List{T}"/>,
/// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>) inside
/// the state graph undermine these guarantees.
/// </para>
/// </typeparam>
public interface IStateManager<T>
    where T : class, IEquatable<T>
{
    /// <summary>
    /// Gets or sets the grain's state snapshot: the loaded or successfully written value,
    /// a configured default when no persisted record exists, or a value assigned here and
    /// not yet written. Defaults are not automatically written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safe to read from <c>[AlwaysInterleave]</c> methods — never returns an
    /// in-flight write candidate, meaning a value whose durability is unknown because
    /// a write is still running. This is the committed-state fence that justifies the
    /// wrapper over raw <see cref="IPersistentState{TState}"/>.
    /// </para>
    /// <para>
    /// A value <em>assigned</em> here is not a write candidate: its durability is
    /// knowingly deferred, not unknown, so it is exposed and flagged by
    /// <see cref="HasUnsavedChanges"/> until <see cref="SaveChangesAsync"/> persists it.
    /// An interleaved reader can therefore observe — and answer from — a value that
    /// disappears if the activation ends without persisting it. Any reply derived from an
    /// unsaved value is only as durable as the next write.
    /// </para>
    /// <para>
    /// Assignment behaves like <see cref="IPersistentState{TState}"/>: it changes what the
    /// grain sees and nothing else. Use <see cref="WriteAsync(T, CancellationToken)"/>
    /// instead when the value must not become visible unless it persisted.
    /// </para>
    /// </remarks>
    T State { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="State"/> has been assigned a value that
    /// has not reached durable storage yet.
    /// </summary>
    /// <remarks>
    /// Cleared by every operation that settles the durability question, including the
    /// ones that settle it by failing: a successful write or clear adopts the new value,
    /// a successful read adopts what storage holds, and a write that failed reverts
    /// <see cref="State"/> to the last stored snapshot. It is therefore never
    /// <c>true</c> for a value that storage has either accepted or definitively rejected.
    /// An operation that settles nothing changes nothing, so the unsaved value stays
    /// visible and flagged and the call can simply be retried: that covers an
    /// already-canceled token, a rejected argument, and a <see cref="ReadAsync"/> whose
    /// storage read throws, which learns nothing about a value it never wrote.
    /// </remarks>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Re-reads state from durable storage, replacing the current
    /// <see cref="State"/> snapshot. Missing records use the configured default
    /// without writing it. Runtime configuration applies to the adopted instance.
    /// </summary>
    /// <remarks>
    /// Not required during activation — <see cref="IPersistentState{TState}"/>
    /// auto-hydrates before <c>OnActivateAsync</c>. Use only when the grain
    /// needs to force a re-read mid-activation (e.g., after a known external
    /// mutation or to recover from a double-failure scenario).
    /// <para>
    /// Storage wins: once the read <em>succeeds</em>, any unsaved value is discarded and
    /// <see cref="HasUnsavedChanges"/> is cleared. A read is an explicit request for what
    /// storage holds, so silently keeping an unpersisted snapshot on top of it would be
    /// the greater surprise. A read whose storage call throws, or that never starts
    /// because the token was already canceled, settles nothing and leaves the unsaved value
    /// and its marker untouched, so it can simply be retried. A read that returns settles
    /// the question, so the marker clears before the returned record is resolved — an
    /// invalid record or a throwing default-state factory is reported to the caller and
    /// does not resurrect the unsaved value.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token for the storage operation.</param>
    Task ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically writes <paramref name="newState"/> to durable storage.
    /// On success, <see cref="State"/> reflects the written value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <paramref name="newState"/> derives from <see cref="VersionedState"/>,
    /// a copy is stamped with a fresh <see cref="Guid"/> version (v7) before
    /// writing. The caller's record keeps its original version; read the
    /// persisted version from <see cref="State"/> after the write.
    /// </para>
    /// <para>
    /// <b>Recovery on failure:</b> Re-reads from storage. If the write actually
    /// landed (version or equality match), swallows the exception and returns
    /// normally. If it did not land, adopts the persisted state before
    /// rethrowing the original exception so the state remains paired with the
    /// ETag refreshed by the read.
    /// <c>InconsistentStateException</c> always rethrows
    /// even if equality matches — a coincidental match must not hide a real
    /// concurrent write.
    /// </para>
    /// <para>
    /// <b>Double failure:</b> If both write and re-read fail, reverts
    /// <see cref="State"/> to the last stored value and rethrows. The grain
    /// holds correct data but a stale ETag — call <see cref="ReadAsync"/>
    /// before the next write.
    /// </para>
    /// <para>
    /// <b>Unsaved changes:</b> once the write reaches storage, <paramref name="newState"/>
    /// wins and <see cref="HasUnsavedChanges"/> is cleared whatever the outcome. On failure
    /// the unsaved value is discarded along with the attempted one, because the value a
    /// write falls back to must be one that storage actually holds. For an outbox that
    /// costs one redelivery of the already-delivered batch, which is self-correcting.
    /// An already-canceled token prevents the write, and then leaves the unsaved value and
    /// its marker untouched.
    /// </para>
    /// </remarks>
    /// <param name="newState">The new state value to persist.</param>
    /// <param name="cancellationToken">The cancellation token for the storage operation and recovery read.</param>
    Task WriteAsync(T newState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the current <see cref="State"/> to durable storage, or does nothing when
    /// <see cref="HasUnsavedChanges"/> is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Equivalent to <c>WriteAsync(State, cancellationToken)</c> when there is something
    /// to save, including version stamping and the full recovery behavior described on
    /// that method. The difference is the guard, which is what makes this one safe to
    /// call unconditionally from a deactivation hook — and that the value is already
    /// visible, where <see cref="WriteAsync(T, CancellationToken)"/> publishes only on
    /// success.
    /// </para>
    /// <para>
    /// With nothing to save this touches neither storage nor
    /// <paramref name="cancellationToken"/>: it is a no-op, not a canceled operation.
    /// Deactivation tokens are routinely already canceled during silo shutdown, and a
    /// grain with nothing outstanding should not have to handle an exception for it.
    /// </para>
    /// <para>
    /// For a <see cref="VersionedState"/>, the manager stamps a copy of the
    /// currently visible state. The staged instance and any references held by
    /// callers retain their original version. While the write is in flight,
    /// <see cref="State"/> continues to expose that staged instance; the stamped
    /// copy becomes visible only after the write is known to have persisted.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    /// {
    ///     try
    ///     {
    ///         await stateManager.SaveChangesAsync(cancellationToken);   // no-op when nothing is unsaved
    ///     }
    ///     catch (Exception ex)
    ///     {
    ///         // Deactivation is not retried, and its token can already be canceled on a
    ///         // forced shutdown, so this write can fail. Losing the value costs a
    ///         // redelivery; letting the failure escape costs the rest of deactivation.
    ///         logger.LogWarning(ex, "Could not save state while deactivating.");
    ///     }
    ///
    ///     await base.OnDeactivateAsync(reason, cancellationToken);
    /// }
    /// </code>
    /// <para>
    /// Unconditional on purpose. Filtering on <see cref="DeactivationReason"/> looks
    /// tempting — skipping <c>ShuttingDown</c>, say — but every reason code that skips a
    /// write is a reason code that drops unsaved data, and a silo shutdown is an orderly,
    /// expected event on every deployment.
    /// </para>
    /// <para>
    /// <b>Live migration is one of those reasons.</b> This library takes no part in
    /// Orleans' migration handoff, so a migrating activation must persist like any other.
    /// Orleans runs <c>OnDeactivateAsync</c> before it dehydrates, so a write here lands
    /// first and the destination inherits a value storage genuinely holds. Skip it and the
    /// unsaved value still rides along inside the storage facet Orleans carries, but the
    /// destination has no way to know it was never written: it treats the value as durable
    /// and loses it at its own next deactivation, or — for an assignment made before the
    /// grain's first write — resolves the configured default and loses it on arrival.
    /// </para>
    /// </example>
    /// <param name="cancellationToken">The cancellation token for the storage operation and recovery read.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the persisted state. On success, <see cref="State"/> reflects
    /// a fresh configured default without writing that default to storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For failures whose outcome is ambiguous, the manager reads storage to
    /// determine whether the clear landed. A missing record confirms a
    /// non-conflicting clear, but an <c>InconsistentStateException</c> always
    /// rethrows because a coincidental deletion must not hide an optimistic
    /// concurrency conflict.
    /// </para>
    /// <para>
    /// When recovery successfully reads a provider value before rethrowing,
    /// <see cref="State"/> adopts that value and can therefore change even
    /// though this method throws. If recovery also fails, <see cref="State"/>
    /// reverts to the last stored value, which is not the pre-clear value when there
    /// were unsaved changes.
    /// </para>
    /// <para>
    /// Storage is cleared before the default-state factory runs. The factory must
    /// return a valid, non-null state. If it throws or returns null, the exception
    /// propagates and the completed deletion is not undone. No usable-state guarantee
    /// applies after this factory contract violation: State may still reference the
    /// previous snapshot, which must not be treated as the current persisted state.
    /// A null result is rejected with an InvalidOperationException for diagnosis.
    /// </para>
    /// <para>
    /// Once the clear reaches storage, any unsaved value is discarded and
    /// <see cref="HasUnsavedChanges"/> is cleared in every branch: a clear settles the
    /// durability question either way, and the marker is cleared before the default-state
    /// factory runs so that a failing factory cannot leave an unsaved value waiting to be
    /// written back over the deleted record. An already-canceled token prevents the clear,
    /// and then leaves the unsaved value and its marker untouched.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token for the storage operation and recovery read.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
