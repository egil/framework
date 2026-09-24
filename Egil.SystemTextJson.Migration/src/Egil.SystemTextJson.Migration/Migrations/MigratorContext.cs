using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record MigratorContext(
    JsonTypeInfo TargetTypeInfo,
    TypeMetadata TargetMetadata,
    MigratorReference[] Migrators,
    string[] SourceDiscriminatorPropertyNames,
    MigratorReference? UndiscriminatedSourceMigrator,
    JsonMigrationFailureHandling MigrationFailureHandling,
    JsonMigrationTypeInfoResolver Resolver)
{
    public bool HasDistinctDiscriminatorProperties { get; } = SourceDiscriminatorPropertyNames
        .Any(name => !StringComparer.Ordinal.Equals(name, TargetMetadata.DiscriminatorPropertyName));

    public byte[] TargetDiscriminatorPropertyNameUtf8 { get; } = System.Text.Encoding.UTF8.GetBytes(TargetMetadata.DiscriminatorPropertyName);

    // Pre-encoded target discriminator value for zero-allocation comparison in the happy path.
    public byte[] TargetDiscriminatorUtf8 { get; } = System.Text.Encoding.UTF8.GetBytes(TargetMetadata.Discriminator);

    public byte[][] SourceDiscriminatorPropertyNameUtf8 { get; } =
        SourceDiscriminatorPropertyNames
            .Select(System.Text.Encoding.UTF8.GetBytes)
            .ToArray();
}
