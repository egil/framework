using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record MigratorReference(
    Type SourceType,
    TypeMetadata SourceMetadata,
    JsonTypeInfo SourceTypeInfo,
    IMigratorInvoker Invoker,
    TypeMetadata? ElementMetadata,
    bool ElementAcceptsNonObjectShapes)
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
    public bool AllowsQuotedNumbers { get; } = SourceValueShapes.AllowsQuotedNumbers(SourceTypeInfo.NumberHandling ?? SourceTypeInfo.Options.NumberHandling, SourceType);

    public bool AllowsNamedFloatingPointLiterals { get; } = SourceValueShapes.AllowsNamedFloatingPointLiterals(SourceTypeInfo.NumberHandling ?? SourceTypeInfo.Options.NumberHandling, SourceType);

    // Elements follow the collection's number handling, then the options'. A NumberHandling set
    // on the element type's own JsonTypeInfo is not applied by STJ's collection converters
    // (verified: List<int> still rejects ["42"] with JsonTypeInfo<int>.NumberHandling set).
    public bool ElementAllowsQuotedNumbers { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && SourceValueShapes.AllowsQuotedNumbers(EffectiveElementNumberHandling(SourceTypeInfo), SourceValueShapes.GetValueType(SourceTypeInfo));

    public bool ElementAllowsNamedFloatingPointLiterals { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && SourceValueShapes.AllowsNamedFloatingPointLiterals(EffectiveElementNumberHandling(SourceTypeInfo), SourceValueShapes.GetValueType(SourceTypeInfo));

    // The element's own contract kind decides whether it is written as a JSON array or object;
    // a CLR heuristic would miss derived collections such as class IntCollection : List<int>.
    // A migratable element is served by the migration converter (Kind None) but is always a JSON
    // object, because the converter factory rejects any other contract kind for such types.
    public JsonTypeInfoKind ElementKind { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? JsonMigratableTypes.IsMigratable(SourceValueShapes.GetValueType(SourceTypeInfo))
            ? JsonTypeInfoKind.Object
            : SourceTypeInfo.Options.GetTypeInfo(SourceValueShapes.GetValueType(SourceTypeInfo)).Kind
        : JsonTypeInfoKind.None;

    public Type ElementType { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        ? SourceValueShapes.GetValueType(SourceTypeInfo)
        : typeof(object);

    // Element discriminators come from the registry (so builder-configured resolvers and
    // property names apply) and are UTF-8 encoded once, so collection disambiguation performs
    // no attribute lookups or allocations while reading.
    public byte[]? ElementDiscriminatorPropertyNameUtf8 { get; } = ElementMetadata is null
        ? null
        : System.Text.Encoding.UTF8.GetBytes(ElementMetadata.DiscriminatorPropertyName);

    public byte[]? ElementDiscriminatorUtf8 { get; } = ElementMetadata is null
        ? null
        : System.Text.Encoding.UTF8.GetBytes(ElementMetadata.Discriminator);

    // An overridden element converter, a union element (.NET 11) whose classifier may accept any
    // token, or a migratable element that itself migrates from a non-object source can read more
    // than the object shape, so the collection is excluded from element-based disambiguation
    // entirely.
    public bool ElementConverterOverridden { get; } = ElementAcceptsNonObjectShapes
        || (SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
            && ElementMayAcceptAnyShape(SourceValueShapes.GetValueType(SourceTypeInfo), SourceTypeInfo.Options));

    public SourceValueShape ElementShape { get; } = SourceTypeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
        && !JsonMigratableTypes.HasConverterOverride(SourceValueShapes.GetValueType(SourceTypeInfo), SourceTypeInfo.Options)
        ? SourceValueShapes.Classify(SourceValueShapes.GetValueType(SourceTypeInfo))
        : SourceValueShape.Unknown;

    private static bool ElementMayAcceptAnyShape(Type elementType, System.Text.Json.JsonSerializerOptions options)
    {
        // object, JsonElement, JsonDocument and JsonNode elements read every JSON token.
        if (elementType == typeof(object)
            || elementType == typeof(System.Text.Json.JsonElement)
            || elementType == typeof(System.Text.Json.JsonDocument)
            || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(elementType)
            || JsonMigratableTypes.HasConverterOverride(elementType, options))
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

    /// <summary>
    /// Whether a collection source's migratable element type migrates from any source that is
    /// not a JSON object (a primitive, array or dictionary), in which case its converter accepts
    /// elements of those shapes too.
    /// </summary>
    public static bool ResolveElementAcceptsNonObjectShapes(Type sourceType, JsonTypeInfo sourceTypeInfo, JsonMigrationRegistry registry)
    {
        if (sourceTypeInfo.Kind is not (JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary))
        {
            return false;
        }

        Type elementType = SourceValueShapes.GetValueType(sourceTypeInfo);
        if (!JsonMigratableTypes.IsMigratable(elementType))
        {
            return false;
        }

        IEnumerable<Type> elementSources = StaticMigratorContracts.GetSourceTypes(elementType)
            .Concat(registry.GetForTarget(elementType).Select(static registration => registration.SourceType));

        foreach (Type elementSource in elementSources)
        {
            // Dictionaries serialize as JSON objects, so only primitive and array sources widen
            // the element's accepted shapes beyond the object shape.
            if (!JsonMigratableTypes.IsMigratable(elementSource)
                && sourceTypeInfo.Options.GetTypeInfo(elementSource).Kind is not (JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the registry metadata of a collection source's migratable element type, or
    /// <see langword="null"/> when the source is not a collection or its elements are not migratable.
    /// </summary>
    public static TypeMetadata? ResolveElementMetadata(Type sourceType, JsonTypeInfo sourceTypeInfo, JsonMigrationRegistry registry)
    {
        if (sourceTypeInfo.Kind is not (JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary))
        {
            return null;
        }

        // An element served by another converter is not written by the migration converter, so
        // its discriminator must not select the collection (the element would bypass migration).
        Type elementType = SourceValueShapes.GetValueType(sourceTypeInfo);
        return JsonMigratableTypes.IsMigratable(elementType) && !JsonMigratableTypes.HasConverterOverride(elementType, sourceTypeInfo.Options)
            ? registry.GetTypeMetadata(elementType)
            : null;
    }
}
