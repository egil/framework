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

    // AllowReadingFromString (on by default with JsonSerializerDefaults.Web) lets numeric sources
    // read quoted numbers; resolved once so the read path does not consult the options.
    public bool AllowsQuotedNumbers { get; } =
        ((SourceTypeInfo.NumberHandling ?? SourceTypeInfo.Options.NumberHandling) & System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString) != 0;

    public Type ElementType { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind)
        : typeof(object);

    // Element discriminators are resolved and UTF-8 encoded once so collection disambiguation
    // performs no attribute lookups or allocations while reading.
    public byte[]? ElementDiscriminatorPropertyNameUtf8 { get; } = ElementMetadata(SourceType, SourceTypeInfo.Kind) is { } metadata
        ? System.Text.Encoding.UTF8.GetBytes(metadata.DiscriminatorPropertyName)
        : null;

    public byte[]? ElementDiscriminatorUtf8 { get; } = ElementMetadata(SourceType, SourceTypeInfo.Kind) is { } metadata
        ? System.Text.Encoding.UTF8.GetBytes(metadata.Discriminator)
        : null;

    public SourceValueShape ElementShape { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? SourceValueShapes.Classify(SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind))
        : SourceValueShape.Unknown;

    private static TypeMetadata? ElementMetadata(Type sourceType, JsonTypeInfoKind kind)
    {
        if (kind is not (JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary))
        {
            return null;
        }

        Type elementType = SourceValueShapes.GetValueType(sourceType, kind);
        return JsonMigratableTypes.IsMigratable(elementType) ? TypeMetadata.FromType(elementType) : null;
    }
}
