using System.Text.Json;

namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Thrown when multiple migrator source types resolve to the same discriminator for one migration target type.
/// </summary>
public sealed class JsonMigrationDuplicateTypeDiscriminatorException : JsonException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMigrationDuplicateTypeDiscriminatorException"/> class.
    /// </summary>
    public JsonMigrationDuplicateTypeDiscriminatorException(
        Type targetType,
        string discriminator,
        Type existingSourceType,
        Type duplicateSourceType)
        : base(
            $"Duplicate type discriminator '{discriminator}' detected for target '{targetType.FullName}'. " +
            $"Source types '{existingSourceType.FullName}' and '{duplicateSourceType.FullName}' resolve to the same discriminator.")
    {
        TargetType = targetType;
        Discriminator = discriminator;
        ExistingSourceType = existingSourceType;
        DuplicateSourceType = duplicateSourceType;
    }

    /// <summary>
    /// Gets the target type for which the collision was detected.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets the discriminator value that collided.
    /// </summary>
    public string Discriminator { get; }

    /// <summary>
    /// Gets the source type that was already mapped to <see cref="Discriminator"/>.
    /// </summary>
    public Type ExistingSourceType { get; }

    /// <summary>
    /// Gets the source type that introduced the duplicate mapping.
    /// </summary>
    public Type DuplicateSourceType { get; }
}
