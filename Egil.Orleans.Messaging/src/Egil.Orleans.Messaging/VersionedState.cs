using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Abstract base for version-stamped grain state. Provides the
/// <see cref="Version"/> property that <see cref="IStateManager{T}"/>
/// uses for its write-recovery comparison.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IStateManager{T}"/> detects this base type at runtime via
/// pattern matching (<c>if (newState is VersionedState v)</c>). If the state
/// derives from <see cref="VersionedState"/>, the manager stamps a fresh
/// <see cref="Guid.CreateVersion7()"/> on <see cref="Version"/> before every
/// write and compares <see cref="Version"/> directly on the recovery path.
/// This means the recovery comparison does <em>not</em> use
/// <c>T.Equals()</c> for <see cref="VersionedState"/>-derived types, avoiding
/// problems with types like <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// whose <c>Equals</c> uses reference equality. For non-<see cref="VersionedState"/>
/// types the manager falls back to <c>T.Equals()</c>.
/// </para>
/// <para>
/// <see cref="Version"/> has <c>internal set</c> — library code can stamp it;
/// user code cannot. This is a hard compile-time fence. The <c>set</c> (not
/// <c>init</c>) accessor allows mutation of the caller's reference during
/// <see cref="IStateManager{T}.WriteAsync"/>.
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// [GenerateSerializer]
/// public sealed record MyState : VersionedState
/// {
///     [Id(0)] public Outbox&lt;MyEvent&gt; Outbox { get; init; }
///     [Id(1)] public ImmutableArray&lt;Something&gt; Items { get; init; } = [];
/// }
/// </code>
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("egil.orleans.messaging.VersionedState")]
public abstract record VersionedState
{
    /// <summary>
    /// Gets the version identifier stamped by <see cref="IStateManager{T}"/>
    /// on every successful write. UUID v7, sortable, collision-free at grain
    /// write rates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fresh records get a non-empty version from the initializer. The manager
    /// overwrites it with a new v7 UUID before each <c>WriteAsync</c> call.
    /// </para>
    /// <para>
    /// For System.Text.Json serialization: the <c>internal set</c> accessor
    /// is invisible to STJ by default. The <see cref="JsonIncludeAttribute"/>
    /// makes it visible without requiring a custom converter.
    /// </para>
    /// </remarks>
    [Id(0)]
    [JsonInclude]
    public Guid Version { get; internal set; } = Guid.CreateVersion7();
}
