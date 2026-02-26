namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Marks a type as migration-aware during JSON serialization and deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class JsonMigratableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the JSON property name used to store the type discriminator.
    /// </summary>
    public string TypeDiscriminatorPropertyName { get; set; } = "$type";

    /// <summary>
    /// Gets or sets the discriminator value to write/read for the annotated type.
    /// </summary>
    public string? TypeDiscriminator { get; set; }
}
