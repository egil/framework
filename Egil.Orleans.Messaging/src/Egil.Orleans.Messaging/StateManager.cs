namespace Egil.Orleans.Messaging;

/// <summary>
/// Default implementation of <see cref="IStateManager{T}"/>. Wraps an
/// <see cref="IPersistentState{T}"/> and adds committed-state fencing,
/// version stamping for <see cref="VersionedState"/>-derived types, and
/// automatic write recovery.
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
/// <b>Concurrency:</b> <see cref="WritePolicy.Concurrent"/> (default) uses
/// the storage provider's ETag for optimistic concurrency. Writes against
/// stale ETags fail with <c>InconsistentStateException</c>.
/// <see cref="WritePolicy.Force"/> nulls the ETag before writing, bypassing
/// the concurrency check — use only for admin repair operations.
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
internal sealed class StateManager<T> : IStateManager<T>
    where T : class, IEquatable<T>
{
    private readonly IPersistentState<T> storage;

    /// <summary>
    /// Creates a new <see cref="StateManager{T}"/> wrapping the given
    /// <paramref name="storage"/> facet.
    /// </summary>
    /// <param name="storage">
    /// The Orleans persistent state facet to wrap. The manager takes full
    /// ownership — callers must not read or write <paramref name="storage"/>
    /// directly after wrapping.
    /// </param>
    internal StateManager(IPersistentState<T> storage)
    {
        this.storage = storage;
    }

    /// <inheritdoc/>
    public T State
    {
        get => throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task ReadAsync()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task WriteAsync(T newState, WritePolicy policy = WritePolicy.Concurrent)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task ClearAsync()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Determines whether the persisted state matches the attempted write.
    /// For <see cref="VersionedState"/>-derived types, compares
    /// <see cref="VersionedState.Version"/> directly. For plain types, falls
    /// back to <see cref="IEquatable{T}.Equals(T)"/>.
    /// </summary>
    private static bool IsEquivalent(T persisted, T attempted)
    {
        throw new NotImplementedException();
    }
}
