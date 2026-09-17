namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Implemented by a grain state type that needs transient dependencies restored on
/// every instance an <see cref="IStateManager{T}"/> adopts.
/// </summary>
/// <remarks>
/// <para>
/// A state type that reaches storage loses everything outside its serialized shape:
/// a <see cref="TimeProvider"/>, a logger, anything resolved from DI. The manager
/// calls <see cref="Configure"/> on each instance it adopts — after activation
/// hydration, after every <see cref="IStateManager{T}.ReadAsync"/>, after a
/// recovery read, and on a default created for an absent record. Publishing the
/// snapshot and configuring it happen in the same turn, with no await between them,
/// so an operation that returns leaves <see cref="IStateManager{T}.State"/> fully
/// wired. A <see cref="Configure"/> that throws is the exception, and the one case
/// where an incompletely wired instance stays visible.
/// </para>
/// <para>
/// This is the same contract as the <c>configureState</c> callback on
/// <c>RegisterStateManager</c>, expressed on the state type instead of at the call
/// site. It is the only way to express the need when the manager is injected with
/// <c>[PersistentState]</c>, because there is no call site to pass a callback to.
/// Prefer it generally: the need belongs to the state type, so every grain holding
/// that state needs the same wiring, and putting it here means no call site can
/// forget it. When both are present the state's own configuration runs first and
/// the callback layers on top.
/// </para>
/// <para>
/// <b>Contract:</b> configure runtime dependencies only. Do not change persisted
/// business values and do not perform storage I/O. An exception thrown here reaches
/// the caller of the operation that adopted the instance, and the adopted snapshot
/// stays visible; a successful storage operation is not retried because
/// configuration failed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [GenerateSerializer]
/// public sealed record OrderState : IConfigurableState
/// {
///     [NonSerialized] private TimeProvider? clock;
///
///     [Id(0)] public MessageTracker Tracker { get; init; } = new();
///
///     public void Configure(IGrainContext context)
///     {
///         clock = context.ActivationServices.GetRequiredService&lt;TimeProvider&gt;();
///         Tracker.RegisterTimeProvider(clock);
///     }
/// }
/// </code>
/// </example>
public interface IConfigurableState
{
    /// <summary>
    /// Restores transient dependencies on this instance.
    /// </summary>
    /// <param name="context">
    /// The activation this state belongs to. <see cref="IGrainContext.ActivationServices"/>
    /// is the same scope the grain's own constructor is resolved from, so anything the
    /// grain could inject — keyed services included — is reachable here.
    /// </param>
    void Configure(IGrainContext context);
}
