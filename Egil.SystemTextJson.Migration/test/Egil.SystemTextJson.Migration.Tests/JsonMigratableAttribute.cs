namespace Egil.SystemTextJson.Migration.Tests;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public class JsonMigratableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a custom type discriminator property name for the migration type. 
    /// Uses the default <c>'$type'</c> property name if left unset.
    /// </summary>
    public string TypeDiscriminatorPropertyName { get; set; } = "$type";

    /// <summary>
    /// The type discriminator identifier to be used for the serialization
    /// and deserialization of the migration type.
    /// </summary>
    public string? TypeDiscriminator { get; set; }
}
