using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration;

/// <summary>
/// Wraps Orleans state so migration metadata can be tracked alongside the actual state payload.
/// </summary>
/// <typeparam name="TStateType">The current state type expected by the grain.</typeparam>
/// <remarks>
/// This wrapper is intended for use with <c>IPersistentState&lt;Storage&lt;TStateType&gt;&gt;</c>.
/// <see cref="MigratedDuringDeserialization"/> indicates the payload was migrated or read from legacy
/// unversioned JSON, and should generally be written back in current format.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// public sealed class CartGrain : Grain
/// {
///     private readonly IPersistentState<Storage<CartStateV2>> _state;
///
///     public override async Task OnActivateAsync(CancellationToken cancellationToken)
///     {
///         if (_state.State.MigratedDuringDeserialization)
///         {
///             await _state.WriteStateAsync();
///         }
///     }
/// }
/// ]]></code>
/// </example>
[JsonConverter(typeof(StorageJsonConverterFactory))]
public sealed class Storage<TStateType>
{
    /// <summary>
    /// Gets the current state value.
    /// </summary>
    public required TStateType State { get; init; }

    /// <summary>
    /// Gets a value indicating whether migration occurred during deserialization.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> implies the payload should typically be persisted again to emit the latest
    /// storage format.
    /// </remarks>
    public bool MigratedDuringDeserialization { get; init; }
}
