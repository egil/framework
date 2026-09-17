using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record MigratorReference(
    Type SourceType,
    TypeMetadata SourceMetadata,
    JsonTypeInfo SourceTypeInfo,
    IMigratorInvoker Invoker)
{
    // Pre-encoded discriminator value for zero-allocation matching via Utf8JsonReader.ValueTextEquals.
    public byte[] DiscriminatorUtf8 { get; } = System.Text.Encoding.UTF8.GetBytes(SourceMetadata.Discriminator);

    // Pre-cached source type name for telemetry to avoid repeated property access.
    public string SourceTypeName { get; } = SourceType.FullName ?? SourceType.Name;

    // Shapes are classified once here so non-object payload matching never touches
    // reflection on the read path.
    public SourceValueShape SourceShape { get; } = SourceValueShapes.Classify(SourceType);

    public Type ElementType { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind)
        : typeof(object);

    public SourceValueShape ElementShape { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? SourceValueShapes.Classify(SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind))
        : SourceValueShape.Unknown;
}
