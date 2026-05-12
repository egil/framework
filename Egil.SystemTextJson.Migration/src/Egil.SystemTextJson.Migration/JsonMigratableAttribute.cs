namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Marks a type as migration-aware during JSON serialization and deserialization.
/// </summary>
/// <remarks>
/// <para>
/// This attribute cannot be combined with <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>
/// on the same type hierarchy. System.Text.Json's polymorphic infrastructure requires every
/// converter in a polymorphic hierarchy to support its internal metadata protocol, which is
/// not extensible from outside the runtime assembly. Attempting to combine the two throws
/// <see cref="NotSupportedException"/> at type-info configuration or (de)serialization time.
/// </para>
/// <para>
/// See the <c>polymorphism.md</c> recipe for details and recommended workarounds.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class JsonMigratableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the JSON property name used to store the type discriminator.
    /// When not set, the builder default is used, and falls back to <c>$type</c>.
    /// </summary>
    public string? TypeDiscriminatorPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the discriminator value to write/read for the annotated type.
    /// </summary>
    public string? TypeDiscriminator { get; set; }

    /// <summary>
    /// Gets or sets migration failure handling for the annotated target type.
    /// When explicitly set on the attribute usage, this overrides the builder-level handling.
    /// </summary>
    public JsonMigrationFailureHandling MigrationFailureHandling { get; set; } = JsonMigrationFailureHandling.ThrowJsonException;

    /// <summary>
    /// Gets or sets the source type to assume when deserializing an object payload without a recognized discriminator.
    /// When not set, discriminator-less object payloads are deserialized directly as the target type.
    /// </summary>
    public Type? UndiscriminatedSourceType { get; set; }
}
