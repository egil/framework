namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Implemented by a grain state type that supplies its own value for an absent
/// storage record, in place of a public parameterless constructor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreateDefault"/> is called when no persisted record exists — at
/// activation, and again after an <see cref="IStateManager{T}.ClearAsync"/>. The
/// result is not written to storage; the first business operation persists it.
/// </para>
/// <para>
/// This is the same contract as the <c>createInitialState</c> factory on
/// <c>RegisterStateManager</c>, expressed on the state type instead of at the call
/// site, and it is the only way to express a non-trivial default when the manager is
/// injected with <c>[PersistentState]</c>. A factory passed to
/// <c>RegisterStateManager</c> still wins over this, so a grain can override the
/// state type's own answer. Without either, an absent record resolves to
/// <c>new TState()</c>.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing state type.</typeparam>
/// <example>
/// <code>
/// [GenerateSerializer]
/// public sealed record OrderState : IStateDefault&lt;OrderState&gt;
/// {
///     [Id(0)] public Guid Id { get; init; }
///
///     public static OrderState CreateDefault(IGrainContext context) =>
///         new() { Id = context.GrainId.GetGuidKey() };
/// }
/// </code>
/// </example>
public interface IStateDefault<TSelf>
    where TSelf : IStateDefault<TSelf>
{
    /// <summary>
    /// Creates the value representing an absent storage record. Must return a valid,
    /// non-null instance; it is not written to storage.
    /// </summary>
    /// <param name="context">
    /// The activation this state belongs to, so a default can derive from the grain
    /// key, the grain type, or anything resolvable from
    /// <see cref="IGrainContext.ActivationServices"/>.
    /// </param>
    static abstract TSelf CreateDefault(IGrainContext context);
}
