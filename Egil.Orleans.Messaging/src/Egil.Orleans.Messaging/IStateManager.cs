namespace Egil.Orleans.Messaging;

/// <summary>
/// A thin wrapper around <see cref="IPersistentState{TState}"/> that guarantees
/// the grain's observable <see cref="State"/> is never out of sync with what is
/// durably persisted, even when <see cref="WriteAsync"/> fails ambiguously
/// (timeout, network drop, server 5xx, ETag conflict).
/// </summary>
/// <remarks>
/// <para>
/// <b>Committed-state fence:</b> <see cref="State"/> exposes only the last
/// successfully written value. During an in-flight write, the underlying
/// <see cref="IPersistentState{TState}"/>.State already holds the uncommitted
/// value. Methods marked <c>[AlwaysInterleave]</c> that read <see cref="State"/>
/// through this interface are guaranteed to never observe uncommitted state.
/// This is the primary reason <see cref="IStateManager{T}"/> exists as a wrapper
/// rather than extension methods on <see cref="IPersistentState{TState}"/>.
/// </para>
/// <para>
/// <b>Usage:</b> Inject <see cref="IPersistentState{TState}"/> as normal via
/// <c>[PersistentState]</c>, then initialize it during <c>OnActivateAsync</c>:
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
/// previous state and rethrows — the grain must call <see cref="ReadAsync"/>
/// before its next write to refresh the ETag.
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
    /// Gets the last successfully committed state snapshot.
    /// </summary>
    /// <remarks>
    /// Safe to read from <c>[AlwaysInterleave]</c> methods — returns only
    /// committed values, never in-flight uncommitted state. This is the
    /// committed-state fence that justifies the wrapper over raw
    /// <see cref="IPersistentState{TState}"/>.
    /// </remarks>
    T State { get; }

    /// <summary>
    /// Re-reads state from durable storage, replacing the current
    /// <see cref="State"/> snapshot.
    /// </summary>
    /// <remarks>
    /// Not required during activation — <see cref="IPersistentState{TState}"/>
    /// auto-hydrates before <c>OnActivateAsync</c>. Use only when the grain
    /// needs to force a re-read mid-activation (e.g., after a known external
    /// mutation or to recover from a double-failure scenario).
    /// </remarks>
    Task ReadAsync();

    /// <summary>
    /// Atomically writes <paramref name="newState"/> to durable storage.
    /// On success, <see cref="State"/> reflects <paramref name="newState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <paramref name="newState"/> derives from <see cref="VersionedState"/>,
    /// a fresh <see cref="Guid"/> version (v7) is stamped on it before writing.
    /// The caller's reference is mutated — this is a documented contract.
    /// </para>
    /// <para>
    /// <b>Recovery on failure:</b> Re-reads from storage. If the write actually
    /// landed (version or equality match), swallows the exception and returns
    /// normally. If it did not land, rethrows the original exception.
    /// <c>InconsistentStateException</c> always rethrows
    /// even if equality matches — a coincidental match must not hide a real
    /// concurrent write.
    /// </para>
    /// <para>
    /// <b>Double failure:</b> If both write and re-read fail, reverts
    /// <see cref="State"/> to its pre-write value and rethrows. The grain
    /// holds correct data but a stale ETag — call <see cref="ReadAsync"/>
    /// before the next write.
    /// </para>
    /// </remarks>
    /// <param name="newState">The new state value to persist.</param>
    Task WriteAsync(T newState);

    /// <summary>
    /// Clears the persisted state, resetting it to <c>default</c>.
    /// </summary>
    Task ClearAsync();
}
