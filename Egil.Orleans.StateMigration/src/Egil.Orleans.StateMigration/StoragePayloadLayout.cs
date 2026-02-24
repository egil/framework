namespace Egil.Orleans.StateMigration;

/// <summary>
/// Controls how <see cref="Storage{TStateType}"/> is represented in JSON.
/// </summary>
/// <remarks>
/// <see cref="Enveloped"/> is the default and supports any JSON shape for the state value.
/// <see cref="Flattened"/> preserves the legacy object shape so callers can more easily move back to plain
/// System.Text.Json contracts that ignore unknown metadata properties.
/// The enveloped hot path is optimized for normal operations where no migration is required.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// var options = new JsonSerializerOptions()
///     .AddStateMigrationSupport(typePropertyName: "$type", payloadLayout: StoragePayloadLayout.Flattened);
/// ]]></code>
/// </example>
public enum StoragePayloadLayout
{
    /// <summary>
    /// Writes metadata and state as:
    /// <c>{ "&lt;typePropertyName&gt;": "identity", "&lt;valuePropertyName&gt;": ... }</c>.
    /// </summary>
    Enveloped = 0,

    /// <summary>
    /// Writes metadata and state properties in the same object:
    /// <c>{ "&lt;typePropertyName&gt;": "identity", ...state properties... }</c>.
    /// </summary>
    /// <remarks>
    /// This mode only supports state types that serialize as JSON objects.
    /// </remarks>
    Flattened = 1,
}
