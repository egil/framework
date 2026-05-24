namespace Egil.Orleans.Messaging;

/// <summary>
/// Provides the <see cref="AsStateManager{T}"/> extension method that wraps an
/// <see cref="IPersistentState{TState}"/> in an <see cref="IStateManager{T}"/>.
/// </summary>
public static class StateManagerExtensions
{
    /// <summary>
    /// Wraps the given <paramref name="storage"/> in an <see cref="IStateManager{T}"/>
    /// that provides atomic write recovery and a committed-state fence.
    /// See <see cref="IStateManager{T}"/> for full behavioral contract, recovery
    /// semantics, and deep-immutability requirements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call site:</b> Typically called once in <c>OnActivateAsync</c>:
    /// <code>
    /// [PersistentState("state")] IPersistentState&lt;MyState&gt; storage;
    /// IStateManager&lt;MyState&gt; stateManager;
    ///
    /// public override Task OnActivateAsync(CancellationToken ct)
    /// {
    ///     stateManager = storage.AsStateManager();
    ///     // ...
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Important:</b> After calling this method, the grain should not access
    /// <paramref name="storage"/> directly. Doing so bypasses the committed-state
    /// fence. See <see cref="IStateManager{T}"/> remarks for details.
    /// </para>
    /// <para>
    /// <b>Provider-specific overrides (future):</b> In v1, this always returns
    /// the default <c>StateManager&lt;T&gt;</c>. In a future version, this method
    /// will check <c>ActivationServices</c> for a registered
    /// <c>IStateManagerFactory</c> to resolve provider-specific implementations
    /// that can classify exceptions more precisely (e.g., "definitely did not
    /// persist" vs. "unknown outcome") and skip the re-read when safe.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="IStateManager{T}" path="/typeparam"/>
    /// <param name="storage">
    /// The Orleans-managed persistent state facet, typically injected via
    /// <c>[PersistentState("name")]</c>. Must already be hydrated (Orleans
    /// hydrates it during the <c>SetupState</c> lifecycle stage, before
    /// <c>OnActivateAsync</c>).
    /// </param>
    /// <returns>
    /// An <see cref="IStateManager{T}"/> wrapping <paramref name="storage"/>.
    /// </returns>
    public static IStateManager<T> AsStateManager<T>(this IPersistentState<T> storage)
        where T : class, IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new StateManager<T>(storage);
    }
}
