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
    // A converter override (attribute or options-level) may read any token family, so such a
    // source is never shape-matched; it is only reachable through an object discriminator.
    public SourceValueShape SourceShape { get; } = JsonMigratableTypes.HasConverterOverride(SourceType, SourceTypeInfo.Options)
        ? SourceValueShape.Unknown
        : SourceValueShapes.Classify(SourceType);

    // Number handling is resolved once so the read path does not consult the options.
    // AllowReadingFromString (on by default with JsonSerializerDefaults.Web) lets numeric sources
    // read quoted numbers; floating-point sources additionally read "NaN"/"Infinity" under
    // AllowNamedFloatingPointLiterals.
    public bool AllowsQuotedNumbers { get; } = SourceValueShapes.AllowsQuotedNumbers(SourceTypeInfo.NumberHandling ?? SourceTypeInfo.Options.NumberHandling);

    public bool AllowsNamedFloatingPointLiterals { get; } = SourceValueShapes.AllowsNamedFloatingPointLiterals(SourceTypeInfo.NumberHandling ?? SourceTypeInfo.Options.NumberHandling, SourceType);

    // Elements follow the collection's number handling, then the options'. A NumberHandling set
    // on the element type's own JsonTypeInfo is not applied by STJ's collection converters
    // (verified: List<int> still rejects ["42"] with JsonTypeInfo<int>.NumberHandling set).
    public bool ElementAllowsQuotedNumbers { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && SourceValueShapes.AllowsQuotedNumbers(EffectiveElementNumberHandling(SourceTypeInfo));

    public bool ElementAllowsNamedFloatingPointLiterals { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && SourceValueShapes.AllowsNamedFloatingPointLiterals(EffectiveElementNumberHandling(SourceTypeInfo), SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind));

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

    // An overridden element converter, or a union element (.NET 11) whose classifier may accept
    // any token, can read any element shape, so the collection is excluded from element-based
    // disambiguation entirely.
    public bool ElementConverterOverridden { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && ElementMayAcceptAnyShape(SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind), SourceTypeInfo.Options);

    public SourceValueShape ElementShape { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && !JsonMigratableTypes.HasConverterOverride(SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind), SourceTypeInfo.Options)
        ? SourceValueShapes.Classify(SourceValueShapes.GetValueType(SourceType, SourceTypeInfo.Kind))
        : SourceValueShape.Unknown;

    private static bool ElementMayAcceptAnyShape(Type elementType, System.Text.Json.JsonSerializerOptions options)
    {
        if (JsonMigratableTypes.HasConverterOverride(elementType, options))
        {
            return true;
        }

#if NET11_0_OR_GREATER
        return options.GetTypeInfo(elementType).Kind is JsonTypeInfoKind.Union;
#else
        return false;
#endif
    }

    private static System.Text.Json.Serialization.JsonNumberHandling EffectiveElementNumberHandling(JsonTypeInfo sourceTypeInfo)
        => sourceTypeInfo.NumberHandling ?? sourceTypeInfo.Options.NumberHandling;

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
