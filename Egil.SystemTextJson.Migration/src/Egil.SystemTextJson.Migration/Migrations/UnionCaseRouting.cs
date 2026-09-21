#if NET11_0_OR_GREATER
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
    private readonly ShapeRoute quotedNumberRoute;
    private readonly ShapeRoute namedLiteralRoute;
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
        ShapeRoute quotedNumberRoute,
        ShapeRoute namedLiteralRoute,
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
        this.quotedNumberRoute = quotedNumberRoute;
        this.namedLiteralRoute = namedLiteralRoute;
        this.numberRoute = numberRoute;
        this.booleanRoute = booleanRoute;
    }

    public static UnionCaseRouting Build(JsonTypeClassifierContext context, JsonMigratableConverterFactory factory, JsonSerializerOptions options)
    {
        JsonMigrationRegistry registry = factory.Registry;
        var entriesByPropertyName = new Dictionary<string, List<DiscriminatorEntry>>(StringComparer.Ordinal);
        var caseByDiscriminator = new Dictionary<(string PropertyName, string Discriminator), Type>();
        var knownDiscriminators = new List<string>();
        Type? undiscriminatedCase = null;
        Type? unknownShapeCase = null;
        var plainObjectCases = new List<Type>();
        var migratableCases = new List<Type>();
        var nestedUnions = new List<(JsonTypeInfo UnionTypeInfo, Type CaseType)>();
        var arrayCases = new List<Type>();
        var stringCases = new List<Type>();
        var quotedNumberCases = new List<Type>();
        var namedLiteralCases = new List<Type>();
        var numberCases = new List<Type>();
        var booleanCases = new List<Type>();

        foreach (JsonUnionCaseInfo unionCase in context.UnionCases)
        {
            Type caseType = unionCase.CaseType;

            // A nullable migratable struct case (T?) is read through T's converter; JSON null
            // never reaches the classifier, so the case routes exactly like T.
            if (JsonMigratableTypes.GetMigratableType(caseType) is { } migratableType)
            {
                // A converter registered ahead of AddJsonMigrationSupport() (or supplied by the
                // resolver) would win over the migration converter, and routing discriminators to
                // it would bypass migration; refuse the configuration instead. The one legitimate
                // plain converter is the factory's own recursion guard: while it builds the case's
                // converter it resolves the case's object contract in cloned options that exclude
                // the case, and a recursive model (Branch(Node[] Children) with union
                // Node(Branch, Leaf)) configures this union inside that clone.
                bool insideOwnMigration = factory.IsBuildingConverterFor(migratableType);
                JsonConverter caseConverter = options.GetTypeInfo(migratableType).Converter;
                if (!IsMigrationConverter(caseConverter) && !insideOwnMigration)
                {
                    throw new InvalidOperationException(
                        $"Union '{context.DeclaringType.FullName}' case '{caseType.FullName}' is annotated with [JsonMigratable] but is served by converter '{caseConverter.GetType().FullName}'. Call AddJsonMigrationSupport() before registering other converters for the case type.");
                }

                // The union converter deserializes the declared case type, so a converter
                // registered for T? itself would bypass the check above. Only STJ's own nullable
                // wrapper (which delegates to T's converter) is acceptable there.
                JsonConverter declaredConverter = options.GetTypeInfo(caseType).Converter;
                if (caseType != migratableType && declaredConverter.GetType().Assembly != typeof(JsonSerializer).Assembly)
                {
                    throw new InvalidOperationException(
                        $"Union '{context.DeclaringType.FullName}' case '{caseType.FullName}' is served by converter '{declaredConverter.GetType().FullName}' instead of the built-in nullable wrapper around the migration converter for '{migratableType.FullName}', so its payloads would bypass migration.");
                }

                migratableCases.Add(caseType);
                TypeMetadata targetMetadata = registry.GetTypeMetadata(migratableType);
                AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, targetMetadata, caseType);

                if (insideOwnMigration)
                {
                    // Current payloads of the case read correctly through its plain contract, but
                    // a payload of one of its sources cannot be migrated here: the migration
                    // converter is unreachable inside its own build. Such a payload only occurs
                    // when a source type references the current union for nested nodes; refuse it
                    // explicitly instead of reading it as the current version and dropping data.
                    foreach (Type sourceType in StaticMigratorContracts.GetSourceTypes(migratableType))
                    {
                        AddRefusedSourceRoute(sourceType, registry.GetTypeMetadata(sourceType), caseType);
                    }

                    foreach (ExternalMigratorRegistration registration in registry.GetForTarget(migratableType))
                    {
                        AddRefusedSourceRoute(registration.SourceType, registration.SourceMetadata, caseType);
                    }

                    if (targetMetadata.UndiscriminatedSourceType is not null)
                    {
                        unknownShapeCase ??= caseType;
                    }

                    continue;
                }

                // Every source type that migrates into this case is routed here too, so the
                // case's own converter can perform the migration. Object sources carry a
                // discriminator; array, dictionary and primitive sources are routed by shape.
                foreach (Type sourceType in StaticMigratorContracts.GetSourceTypes(migratableType))
                {
                    AddSourceRoute(sourceType, registry.GetTypeMetadata(sourceType), caseType);
                }

                foreach (ExternalMigratorRegistration registration in registry.GetForTarget(migratableType))
                {
                    AddSourceRoute(registration.SourceType, registration.SourceMetadata, caseType);
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
            if (JsonMigratableTypes.HasConverterOverride(caseType, options))
            {
                // Same treatment as an overridden source: reachable through its discriminator
                // (default: the type's full name) when the type can carry one, never by shape.
                if (!IsScalarType(caseType))
                {
                    AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, registry.GetTypeMetadata(caseType), caseType);
                }

                unknownShapeCase ??= caseType;
                continue;
            }

            AddShapeRoute(caseType, caseType);
        }

        void AddSourceRoute(Type sourceType, TypeMetadata sourceMetadata, Type caseType)
        {
            // The override check comes first: a [JsonMigratable] source served by another
            // converter (a resolver-attached one, for example) is not written by the migration
            // converter, so its discriminator route would be meaningless.
            if (JsonMigratableTypes.HasConverterOverride(sourceType, options))
            {
                // The overriding converter may read any token family, so the source is reachable
                // only through its discriminator and every discriminator-less payload is refused.
                // Scalar CLR types (primitives, enums, string-shaped types) cannot carry a
                // discriminator, so no route is advertised for them; objects and collections
                // may be read from a discriminated object by their converter and keep it.
                if (!IsScalarType(sourceType))
                {
                    AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, sourceMetadata, caseType);
                }

                unknownShapeCase ??= caseType;
                return;
            }

            // A migratable source (or a Nullable<T> of one) is served by the migration converter
            // (Kind None) and is always identified by its discriminator, like any object source.
            if (JsonMigratableTypes.GetMigratableType(sourceType) is not null)
            {
                AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, sourceMetadata, caseType);
                return;
            }

            switch (options.GetTypeInfo(Nullable.GetUnderlyingType(sourceType) ?? sourceType).Kind)
            {
                case JsonTypeInfoKind.Object:
                    AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, sourceMetadata, caseType);
                    return;
                case JsonTypeInfoKind.Union:
                    // The migration converter identifies a source by the source type's own
                    // discriminator, which a union payload never carries, so a union-typed source
                    // cannot be selected; it only guards against shape-based fallback.
                    unknownShapeCase ??= caseType;
                    return;
                default:
                    AddShapeRoute(sourceType, caseType);
                    return;
            }
        }

        // Mirrors AddSourceRoute for a case inside its own migration: sources that would be
        // routed by discriminator get a refusing entry, sources routed by shape make every
        // discriminator-less payload refused.
        void AddRefusedSourceRoute(Type sourceType, TypeMetadata sourceMetadata, Type caseType)
        {
            bool overridden = JsonMigratableTypes.HasConverterOverride(sourceType, options);
            bool discriminated = overridden
                ? !IsScalarType(sourceType)
                : JsonMigratableTypes.GetMigratableType(sourceType) is not null
                    || options.GetTypeInfo(Nullable.GetUnderlyingType(sourceType) ?? sourceType).Kind is JsonTypeInfoKind.Object;

            if (discriminated)
            {
                AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, sourceMetadata, RefusedInsideOwnMigration.For(caseType));
            }
            else
            {
                unknownShapeCase ??= caseType;
            }
        }

        void AddShapeRoute(Type shapeType, Type caseType)
        {
            switch (SourceValueShapes.Classify(shapeType))
            {
                case SourceValueShape.String:
                    AddCase(stringCases, caseType);
                    return;
                case SourceValueShape.Number:
                    AddCase(numberCases, caseType);

                    // With AllowReadingFromString (on by default with JsonSerializerDefaults.Web)
                    // the numeric converter also accepts a quoted number, and floating-point
                    // converters accept "NaN"/"Infinity" under either that flag or
                    // AllowNamedFloatingPointLiterals. A string-shaped case still wins string
                    // payloads; numeric cases only take them when no such case exists.
                    JsonNumberHandling numberHandling = options.GetTypeInfo(shapeType).NumberHandling ?? options.NumberHandling;
                    if (SourceValueShapes.AllowsQuotedNumbers(numberHandling, shapeType))
                    {
                        AddCase(quotedNumberCases, caseType);
                    }

                    if (SourceValueShapes.AllowsNamedFloatingPointLiterals(numberHandling, shapeType))
                    {
                        AddCase(namedLiteralCases, caseType);
                    }

                    return;
                case SourceValueShape.Boolean:
                    AddCase(booleanCases, caseType);
                    return;
            }

            // Dictionaries serialize as JSON objects, so they compete with object cases. A
            // nullable struct is written by the underlying type's contract.
            JsonTypeInfo shapeTypeInfo = options.GetTypeInfo(Nullable.GetUnderlyingType(shapeType) ?? shapeType);
            switch (shapeTypeInfo.Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    AddCase(arrayCases, caseType);
                    break;
                case JsonTypeInfoKind.Object:
                case JsonTypeInfoKind.Dictionary:
                    AddCase(plainObjectCases, caseType);
                    break;
                case JsonTypeInfoKind.Union:
                    // A nested union is reached through the discriminators of its own
                    // migratable cases (recursively), registered after the direct cases so a
                    // direct case keeps a discriminator both claim; for every other shape it may
                    // accept anything, see below.
                    nestedUnions.Add((shapeTypeInfo, caseType));
                    unknownShapeCase ??= caseType;
                    break;
                default:
                    // A custom converter can accept any JSON shape, so no shape-based fallback
                    // is safe while such a case exists: routing a discriminator-less object to
                    // another case could silently drop the data this case would have read.
                    unknownShapeCase ??= caseType;
                    break;
            }
        }

        void AddNestedDiscriminator(TypeMetadata metadata, Type caseType, HashSet<(string PropertyName, string Discriminator)> directClaims)
        {
            if (!directClaims.Contains((metadata.DiscriminatorPropertyName, metadata.Discriminator)))
            {
                AddDiscriminator(context.DeclaringType, entriesByPropertyName, caseByDiscriminator, knownDiscriminators, metadata, caseType);
            }
        }

        // Mirrors AddSourceRoute: only sources that can carry a discriminator are forwarded.
        // Shape-routed sources are covered by the any-shape guard the nested union imposes.
        void AddNestedSource(Type sourceType, TypeMetadata sourceMetadata, Type caseType, HashSet<(string PropertyName, string Discriminator)> directClaims)
        {
            if (JsonMigratableTypes.HasConverterOverride(sourceType, options))
            {
                if (!IsScalarType(sourceType))
                {
                    AddNestedDiscriminator(sourceMetadata, caseType, directClaims);
                }

                return;
            }

            if (JsonMigratableTypes.GetMigratableType(sourceType) is not null
                || options.GetTypeInfo(Nullable.GetUnderlyingType(sourceType) ?? sourceType).Kind is JsonTypeInfoKind.Object)
            {
                AddNestedDiscriminator(sourceMetadata, caseType, directClaims);
            }
        }

        void AddNestedUnionDiscriminators(JsonTypeInfo unionTypeInfo, Type caseType, HashSet<(string PropertyName, string Discriminator)> directClaims)
        {
            foreach (JsonUnionCaseInfo nestedCase in unionTypeInfo.UnionCases)
            {
                Type nestedType = nestedCase.CaseType;
                if (JsonMigratableTypes.GetMigratableType(nestedType) is { } nestedMigratableType)
                {
                    AddNestedDiscriminator(registry.GetTypeMetadata(nestedMigratableType), caseType, directClaims);

                    foreach (Type sourceType in StaticMigratorContracts.GetSourceTypes(nestedMigratableType))
                    {
                        AddNestedSource(sourceType, registry.GetTypeMetadata(sourceType), caseType, directClaims);
                    }

                    foreach (ExternalMigratorRegistration registration in registry.GetForTarget(nestedMigratableType))
                    {
                        AddNestedSource(registration.SourceType, registration.SourceMetadata, caseType, directClaims);
                    }

                    continue;
                }

                // Same rule as for a direct case: an overridden case that can carry a
                // discriminator is reachable through it, so the outer union forwards that route.
                if (JsonMigratableTypes.HasConverterOverride(nestedType, options))
                {
                    if (!IsScalarType(nestedType))
                    {
                        AddNestedDiscriminator(registry.GetTypeMetadata(nestedType), caseType, directClaims);
                    }

                    continue;
                }

                JsonTypeInfo nestedTypeInfo = options.GetTypeInfo(Nullable.GetUnderlyingType(nestedType) ?? nestedType);
                if (nestedTypeInfo.Kind is JsonTypeInfoKind.Union)
                {
                    AddNestedUnionDiscriminators(nestedTypeInfo, caseType, directClaims);
                }
            }
        }

        // Direct cases keep any discriminator a nested union also claims; conflicts between
        // nested unions are still configuration errors.
        var directClaims = new HashSet<(string PropertyName, string Discriminator)>(caseByDiscriminator.Keys);
        foreach ((JsonTypeInfo unionTypeInfo, Type caseType) in nestedUnions)
        {
            AddNestedUnionDiscriminators(unionTypeInfo, caseType, directClaims);
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
            ShapeRoute.From(quotedNumberCases),
            ShapeRoute.From(namedLiteralCases),
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
                return Resolve(ResolveStringRoute(ref reader), "string");
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
                        if (entry.Refused)
                        {
                            ThrowRefusedInsideOwnMigration(ref reader, entry.CaseType);
                        }

                        return entry.CaseType;
                    }
                }

                ThrowUnknownDiscriminator(ref reader);
            }
        }

        // No leading discriminator. Shape-based fallback is refused outright while a case may
        // accept any shape (nested union or converter override), because that case might be the
        // right one. Otherwise prefer the case explicitly configured for undiscriminated
        // objects, then the plain object cases (they can never carry a discriminator), and only
        // then fall back to a lone migratable case, mirroring the converter's legacy-payload rule.
        if (unknownShapeCase is not null)
        {
            ThrowUnknownShapeCase("object", unknownShapeCase);
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

    private ShapeRoute ResolveStringRoute(ref Utf8JsonReader reader)
    {
        if (stringRoute.Kind is not RouteKind.None)
        {
            return stringRoute;
        }

        return SourceValueShapes.IsNamedFloatingPointLiteral(ref reader) ? namedLiteralRoute : quotedNumberRoute;
    }

    private Type Resolve(ShapeRoute route, string shape)
    {
        if (unknownShapeCase is not null)
        {
            ThrowUnknownShapeCase(shape, unknownShapeCase);
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

    // Primitives, enums and string-shaped types cannot carry a discriminator whatever converter
    // reads them. Any other type may: a custom converter decides the JSON shape (an object-writing
    // converter for a numeric struct such as Complex is legitimate), so the route is kept.
    private static bool IsScalarType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || SourceValueShapes.Classify(underlying) is not SourceValueShape.Unknown;
    }

    private static bool IsMigrationConverter(JsonConverter converter)
        => converter.GetType().IsGenericType && converter.GetType().GetGenericTypeDefinition() == typeof(JsonMigratableConverter<>);

    private static void AddCase(List<Type> cases, Type caseType)
    {
        if (!cases.Contains(caseType))
        {
            cases.Add(caseType);
        }
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

        // The marker is resolved here so the read path only tests a flag.
        Type? refusedCase = RefusedInsideOwnMigration.Unwrap(caseType);
        entries.Add(new DiscriminatorEntry(Encoding.UTF8.GetBytes(metadata.Discriminator), refusedCase ?? caseType, refusedCase is not null));
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
    private void ThrowRefusedInsideOwnMigration(ref Utf8JsonReader reader, Type caseType)
    {
        throw new JsonException(
            $"Payload with discriminator '{reader.GetString()}' migrates into '{caseType.FullName}', but it is nested inside the migration of that same type, where the migration converter is unavailable. Model the old version of a recursive type with old types for its nested nodes instead of referencing the current union '{unionType.FullName}'.");
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
    private void ThrowUnknownShapeCase(string shape, Type guard)
    {
        throw new JsonException(
            $"Union '{unionType.FullName}' cannot classify a JSON {shape} without a leading type discriminator because case '{guard.FullName}' may accept it through a custom converter or nested union. Known discriminators: {knownDiscriminatorList}.");
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

    // Marker case type for a discriminator the union knows but must refuse; it doubles as the
    // duplicate-claim key so a refused source still conflicts with another case claiming it.
    private static class RefusedInsideOwnMigration
    {
        public static Type For(Type caseType) => typeof(RefusedInsideOwnMigration<>).MakeGenericType(caseType);

        public static Type? Unwrap(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RefusedInsideOwnMigration<>)
                ? type.GetGenericArguments()[0]
                : null;
    }

#pragma warning disable S2326 // Unused type parameters should be removed - the argument identifies the refused case.
    private sealed class RefusedInsideOwnMigration<TCase>;
#pragma warning restore S2326

    private sealed record DiscriminatorEntry(byte[] DiscriminatorUtf8, Type CaseType, bool Refused);

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
