#if NET11_0_OR_GREATER
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Routing table for one union, built once when its <see cref="JsonTypeInfo"/> is configured.
/// The read path only compares pre-encoded UTF-8 bytes and enum values; it never allocates
/// unless it throws.
/// </summary>
internal sealed class UnionCaseRouting
{
    private readonly Type unionType;
    private readonly DiscriminatorPropertyGroup[] discriminatorGroups;
    private readonly string knownDiscriminatorList;
    private readonly Type? undiscriminatedCase;
    private readonly Type? unknownShapeCase;
    private readonly ShapeRoute objectRoute;
    private readonly ShapeRoute legacyObjectRoute;
    private readonly ShapeRoute arrayRoute;
    private readonly ShapeRoute stringRoute;
    private readonly ShapeRoute numberRoute;
    private readonly ShapeRoute booleanRoute;

    private UnionCaseRouting(
        Type unionType,
        DiscriminatorPropertyGroup[] discriminatorGroups,
        string knownDiscriminatorList,
        Type? undiscriminatedCase,
        Type? unknownShapeCase,
        ShapeRoute objectRoute,
        ShapeRoute legacyObjectRoute,
        ShapeRoute arrayRoute,
        ShapeRoute stringRoute,
        ShapeRoute numberRoute,
        ShapeRoute booleanRoute)
    {
        this.unionType = unionType;
        this.discriminatorGroups = discriminatorGroups;
        this.knownDiscriminatorList = knownDiscriminatorList;
        this.undiscriminatedCase = undiscriminatedCase;
        this.unknownShapeCase = unknownShapeCase;
        this.objectRoute = objectRoute;
        this.legacyObjectRoute = legacyObjectRoute;
        this.arrayRoute = arrayRoute;
        this.stringRoute = stringRoute;
        this.numberRoute = numberRoute;
        this.booleanRoute = booleanRoute;
    }

    public static UnionCaseRouting Build(JsonTypeClassifierContext context, JsonMigrationRegistry registry, JsonSerializerOptions options)
    {
        var entriesByPropertyName = new Dictionary<string, List<DiscriminatorEntry>>(StringComparer.Ordinal);
        var caseByDiscriminator = new Dictionary<(string PropertyName, string Discriminator), Type>();
        var knownDiscriminators = new List<string>();
        Type? undiscriminatedCase = null;
        Type? unknownShapeCase = null;
        var plainObjectCases = new List<Type>();
        var migratableCases = new List<Type>();
        var arrayCases = new List<Type>();
        var stringCases = new List<Type>();
        var numberCases = new List<Type>();
        var booleanCases = new List<Type>();

        foreach (JsonUnionCaseInfo unionCase in context.UnionCases)
        {
            Type caseType = unionCase.CaseType;

            if (JsonMigratableTypes.IsMigratable(caseType))
            {
                migratableCases.Add(caseType);
                TypeMetadata targetMetadata = registry.GetTypeMetadata(caseType);
                AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, targetMetadata, caseType);

                // Every source type that migrates into this case is routed here too, so the
                // case's own converter can perform the migration.
                foreach (Type sourceType in StaticMigratorContracts.GetSourceTypes(caseType))
                {
                    AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, registry.GetTypeMetadata(sourceType), caseType);
                }

                foreach (ExternalMigratorRegistration registration in registry.GetForTarget(caseType))
                {
                    AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, registration.SourceMetadata, caseType);
                }

                if (targetMetadata.UndiscriminatedSourceType is not null)
                {
                    if (undiscriminatedCase is not null)
                    {
                        throw new InvalidOperationException(
                            $"Union '{context.DeclaringType.FullName}' has more than one case configuring '{nameof(JsonMigratableAttribute.UndiscriminatedSourceType)}' ('{undiscriminatedCase.FullName}' and '{caseType.FullName}'), so object payloads without a discriminator cannot be classified.");
                    }

                    undiscriminatedCase = caseType;
                }

                continue;
            }

            // A converter override can change the token family a scalar reads from (for example
            // JsonStringEnumConverter turns a numeric enum into a string), which shape
            // classification cannot see. Such cases only route through a discriminator.
            if (HasConverterOverride(caseType, options))
            {
                unknownShapeCase ??= caseType;
                continue;
            }

            switch (SourceValueShapes.Classify(caseType))
            {
                case SourceValueShape.String:
                    stringCases.Add(caseType);
                    continue;
                case SourceValueShape.Number:
                    numberCases.Add(caseType);
                    continue;
                case SourceValueShape.Boolean:
                    booleanCases.Add(caseType);
                    continue;
            }

            // Dictionaries serialize as JSON objects, so they compete with object cases.
            switch (options.GetTypeInfo(caseType).Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    arrayCases.Add(caseType);
                    break;
                case JsonTypeInfoKind.Object:
                case JsonTypeInfoKind.Dictionary:
                    plainObjectCases.Add(caseType);
                    break;
                default:
                    // A nested union or a custom converter can accept any JSON shape, so no
                    // shape-based fallback is safe while such a case exists: routing a
                    // discriminator-less object to another case could silently drop the data
                    // the nested case would have read. Discriminated payloads still route.
                    unknownShapeCase ??= caseType;
                    break;
            }
        }

        DiscriminatorPropertyGroup[] groups = [.. entriesByPropertyName.Select(static pair => new DiscriminatorPropertyGroup(
            Encoding.UTF8.GetBytes(pair.Key),
            [.. pair.Value]))];

        return new UnionCaseRouting(
            context.DeclaringType,
            groups,
            string.Join(", ", knownDiscriminators.Select(static discriminator => $"'{discriminator}'")),
            undiscriminatedCase,
            unknownShapeCase,
            ShapeRoute.From(plainObjectCases),
            ShapeRoute.From(migratableCases),
            ShapeRoute.From(arrayCases),
            ShapeRoute.From(stringCases),
            ShapeRoute.From(numberCases),
            ShapeRoute.From(booleanCases));
    }

    public Type? Classify(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return ClassifyObject(ref reader);
            case JsonTokenType.StartArray:
                return Resolve(arrayRoute, "array");
            case JsonTokenType.String:
                return Resolve(stringRoute, "string");
            case JsonTokenType.Number:
                return Resolve(numberRoute, "number");
            case JsonTokenType.True:
            case JsonTokenType.False:
                return Resolve(booleanRoute, "boolean");
            default:
                ThrowNoCase(reader.TokenType.ToString());
                return null;
        }
    }

    private Type ClassifyObject(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (reader.TokenType is JsonTokenType.PropertyName)
        {
            foreach (DiscriminatorPropertyGroup group in discriminatorGroups)
            {
                if (!reader.ValueTextEquals(group.PropertyNameUtf8))
                {
                    continue;
                }

                if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
                {
                    throw new JsonException($"Expected discriminator string, got '{reader.TokenType}'.");
                }

                foreach (DiscriminatorEntry entry in group.Entries)
                {
                    if (reader.ValueTextEquals(entry.DiscriminatorUtf8))
                    {
                        return entry.CaseType;
                    }
                }

                ThrowUnknownDiscriminator(ref reader);
            }
        }

        // No leading discriminator: prefer the case explicitly configured for undiscriminated
        // objects, then the plain object cases (they can never carry a discriminator), and only
        // then fall back to a lone migratable case, mirroring the converter's legacy-payload rule.
        if (unknownShapeCase is not null)
        {
            ThrowUnknownShapeCase("object");
        }

        if (undiscriminatedCase is not null)
        {
            return undiscriminatedCase;
        }

        if (objectRoute.Kind is RouteKind.Single)
        {
            return objectRoute.CaseType!;
        }

        if (objectRoute.Kind is RouteKind.None && legacyObjectRoute.Kind is RouteKind.Single)
        {
            return legacyObjectRoute.CaseType!;
        }

        ThrowAmbiguousObject();
        return null!;
    }

    private Type Resolve(ShapeRoute route, string shape)
    {
        if (unknownShapeCase is not null)
        {
            ThrowUnknownShapeCase(shape);
        }

        if (route.Kind is RouteKind.Single)
        {
            return route.CaseType!;
        }

        if (route.Kind is RouteKind.Ambiguous)
        {
            ThrowAmbiguousShape(shape);
        }

        ThrowNoCase(shape);
        return null!;
    }

    private static bool HasConverterOverride(Type caseType, JsonSerializerOptions options)
    {
        if (caseType.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is not null)
        {
            return true;
        }

        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.CanConvert(caseType))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddDiscriminator(
        Type unionType,
        Dictionary<string, List<DiscriminatorEntry>> entriesByPropertyName,
        Dictionary<(string PropertyName, string Discriminator), Type> caseByDiscriminator,
        List<string> knownDiscriminators,
        TypeMetadata metadata,
        Type caseType)
    {
        var key = (metadata.DiscriminatorPropertyName, metadata.Discriminator);
        if (caseByDiscriminator.TryGetValue(key, out Type? existingCase))
        {
            if (existingCase == caseType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Union '{unionType.FullName}' cannot be classified: discriminator '{metadata.Discriminator}' is claimed by both '{existingCase.FullName}' and '{caseType.FullName}'.");
        }

        caseByDiscriminator.Add(key, caseType);
        knownDiscriminators.Add(metadata.Discriminator);

        if (!entriesByPropertyName.TryGetValue(metadata.DiscriminatorPropertyName, out List<DiscriminatorEntry>? entries))
        {
            entries = [];
            entriesByPropertyName.Add(metadata.DiscriminatorPropertyName, entries);
        }

        entries.Add(new DiscriminatorEntry(Encoding.UTF8.GetBytes(metadata.Discriminator), caseType));
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowUnknownDiscriminator(ref Utf8JsonReader reader)
    {
        throw new JsonException(
            $"No case of union '{unionType.FullName}' matches discriminator '{reader.GetString()}'. Known discriminators: {knownDiscriminatorList}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowAmbiguousObject()
    {
        throw new JsonException(
            $"Object payload for union '{unionType.FullName}' has no leading type discriminator and more than one case accepts JSON objects, so the case is ambiguous. Known discriminators: {knownDiscriminatorList}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowUnknownShapeCase(string shape)
    {
        throw new JsonException(
            $"Union '{unionType.FullName}' cannot classify a JSON {shape} without a leading type discriminator because case '{unknownShapeCase!.FullName}' may accept any JSON shape. Known discriminators: {knownDiscriminatorList}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowAmbiguousShape(string shape)
    {
        throw new JsonException(
            $"More than one case of union '{unionType.FullName}' accepts a JSON {shape}, so the payload is ambiguous.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowNoCase(string shape)
    {
        throw new JsonException(
            $"No case of union '{unionType.FullName}' accepts a JSON {shape}. Known discriminators: {knownDiscriminatorList}.");
    }

    private sealed record DiscriminatorPropertyGroup(byte[] PropertyNameUtf8, DiscriminatorEntry[] Entries);

    private sealed record DiscriminatorEntry(byte[] DiscriminatorUtf8, Type CaseType);

    private enum RouteKind
    {
        None,
        Single,
        Ambiguous,
    }

    private readonly record struct ShapeRoute(RouteKind Kind, Type? CaseType)
    {
        public static ShapeRoute From(List<Type> cases)
            => cases.Count switch
            {
                0 => new ShapeRoute(RouteKind.None, null),
                1 => new ShapeRoute(RouteKind.Single, cases[0]),
                _ => new ShapeRoute(RouteKind.Ambiguous, null),
            };
    }
}
#endif
