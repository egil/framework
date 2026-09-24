namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Marks a historical payload type that should only be used to read and migrate old JSON.
/// </summary>
/// <remarks>This marker is consumed by migration analyzers and does not change serialization or migration behavior.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class JsonMigrationLegacyTypeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether the migration is supplied outside the current compilation.
    /// Reserved for orphan-source analysis (STJM0011); this does not suppress legacy-use warnings (STJM0010).
    /// </summary>
    public bool MigratedExternally { get; set; }
}
