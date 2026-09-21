using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Serves migration-aware contracts for types annotated with <see cref="JsonMigratableAttribute"/>
/// from the front of <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
/// </summary>
/// <remarks>
/// Migration is registered as a resolver rather than as an entry in <see cref="JsonSerializerOptions.Converters"/>
/// because System.Text.Json disables source-generated fast-path serialization for every type in the
/// options as soon as that list is non-empty (<c>JsonSerializerContext.IsCompatibleWithOptions</c>
/// requires <c>Converters.Count == 0</c>). A resolver that is not one of STJ's built-in ones only
/// costs the fast path for the contracts it produces itself and for the types whose property graph
/// reaches them; everything else keeps the generated serializer.
/// </remarks>
internal sealed class JsonMigrationTypeInfoResolver : IJsonTypeInfoResolver
{
    private const string TryMigrateFromMethodName = nameof(IMigrateFrom<,>.TryMigrateFrom);

    private readonly JsonMigrationRegistry registry;
    private readonly JsonConverter[] precedingConverters;
    private readonly HashSet<Type> excludedTypes;
    private DefaultJsonTypeInfoResolver? reflectionFallback;

    /// <param name="precedingConverters">
    /// The converters already present in <see cref="JsonSerializerOptions.Converters"/> when migration
    /// support was added. STJ picks the first matching converter from that list, and before this
    /// resolver existed the migration converter factory sat at the position where it was registered, so
    /// a converter registered earlier won over migration and one registered later lost. The resolver
    /// keeps that order by stepping aside for the earlier ones.
    /// </param>
    public JsonMigrationTypeInfoResolver(JsonMigrationRegistry registry, IEnumerable<JsonConverter> precedingConverters)
        : this(registry, [.. precedingConverters], [])
    {
    }

    private JsonMigrationTypeInfoResolver(JsonMigrationRegistry registry, JsonConverter[] precedingConverters, HashSet<Type> excludedTypes)
    {
        this.registry = registry;
        this.precedingConverters = precedingConverters;
        this.excludedTypes = excludedTypes;
    }

    internal JsonMigrationRegistry Registry => registry;

    /// <summary>
    /// Whether this resolver is the type-excluding clone used while <paramref name="type"/>'s own
    /// converter is being built, so that <paramref name="type"/> resolves to its plain object contract.
    /// </summary>
    internal bool IsBuildingConverterFor(Type type) => excludedTypes.Contains(type);

    /// <inheritdoc/>
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        if (excludedTypes.Contains(type))
        {
            // The plain contract of a type whose converter is being built: read and written by STJ's
            // own object converter, with the discriminator property added so it is serialized first.
            JsonTypeInfo? plainTypeInfo = ResolveFromRemainingChain(type, options);
            if (plainTypeInfo is not null)
            {
                AddDiscriminatorProperty(plainTypeInfo, registry.GetTypeMetadata(type));
            }

            return plainTypeInfo;
        }

        if (JsonMigratableTypes.IsMigratable(type) && !HasPrecedingConverter(type))
        {
            return CreateMigrationTypeInfo(type, options);
        }

        return ResolveReflectionFallback(type, options);
    }

    private bool HasPrecedingConverter(Type type)
    {
        foreach (JsonConverter converter in precedingConverters)
        {
            if (converter.CanConvert(type))
            {
                return true;
            }
        }

        return false;
    }

    private JsonTypeInfo? ResolveFromRemainingChain(Type type, JsonSerializerOptions options)
    {
        bool passedSelf = false;
        foreach (IJsonTypeInfoResolver resolver in options.TypeInfoResolverChain)
        {
            if (ReferenceEquals(resolver, this))
            {
                passedSelf = true;
                continue;
            }

            if (passedSelf && resolver.GetTypeInfo(type, options) is { } typeInfo)
            {
                return typeInfo;
            }
        }

        return ResolveReflectionFallback(type, options);
    }

    private JsonTypeInfo? ResolveReflectionFallback(Type type, JsonSerializerOptions options)
    {
        // STJ populates DefaultJsonTypeInfoResolver only when the chain is empty at freeze time, and
        // inserting this resolver makes it non-empty. Users who never configure a resolver would
        // otherwise lose reflection-based serialization, so the resolver stands in for the default
        // when it is the only entry; a context added later takes over as it would without migration.
        if (options.TypeInfoResolverChain.Count != 1 || !JsonSerializer.IsReflectionEnabledByDefault)
        {
            return null;
        }

        reflectionFallback ??= new DefaultJsonTypeInfoResolver();
        return reflectionFallback.GetTypeInfo(type, options);
    }

    private JsonTypeInfo CreateMigrationTypeInfo(Type typeToConvert, JsonSerializerOptions options)
    {
        ValidateTargetMigratorContracts(typeToConvert);

        TypeMetadata targetMetadata = registry.GetTypeMetadata(typeToConvert);

        // Clone options and replace this resolver with a type-excluding instance so metadata lookup can
        // still apply migration converters for nested migratable types while the type itself resolves to
        // its plain object contract. The replacement keeps the resolver's position in the chain.
        // This resolver is in the chain because it is the one being asked, so IndexOf cannot fail.
        var metadataOptions = new JsonSerializerOptions(options);
        var excludingResolver = new JsonMigrationTypeInfoResolver(registry, precedingConverters, new HashSet<Type>(excludedTypes) { typeToConvert });
        IList<IJsonTypeInfoResolver> metadataChain = metadataOptions.TypeInfoResolverChain;
        metadataChain[metadataChain.IndexOf(this)] = excludingResolver;

        JsonTypeInfo targetTypeInfo = GetRequiredTypeInfo(metadataOptions, typeToConvert);

        var migrators = BuildMigratorMap(typeToConvert, metadataOptions);

        var sourcePropertyNames = migrators
            .Select(static migrator => migrator.SourceMetadata.DiscriminatorPropertyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MigratorReference? undiscriminatedSourceMigrator = ResolveUndiscriminatedSourceMigrator(
            typeToConvert,
            targetMetadata,
            migrators);

        // Freeze the metadata options so that internal STJ methods like
        // GetTypeInfoInternal (called by JsonResumableConverter<T>.Read) use the
        // read-only cache path instead of the mutable path which silently returns null
        // when resolveIfMutable defaults to false.
        metadataOptions.MakeReadOnly();

        var context = new MigratorContext(
            targetTypeInfo,
            targetMetadata,
            migrators,
            sourcePropertyNames,
            undiscriminatedSourceMigrator,
            registry.GetMigrationFailureHandling(typeToConvert));

        MethodInfo factoryMethod = typeof(JsonMigrationTypeInfoResolver)
            .GetMethod(nameof(CreateTypeInfo), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeToConvert);

        return (JsonTypeInfo)factoryMethod.Invoke(null, [options, context])!;
    }

    private static JsonTypeInfo CreateTypeInfo<T>(JsonSerializerOptions options, MigratorContext context)
        => JsonMetadataServices.CreateValueInfo<T>(options, new JsonMigratableConverter<T>(context));

    private MigratorReference[] BuildMigratorMap(Type targetType, JsonSerializerOptions metadataOptions)
    {
        var migrators = new Dictionary<string, MigratorCandidate>(StringComparer.Ordinal);

        foreach (ExternalMigratorRegistration registration in registry.GetForTarget(targetType))
        {
            JsonTypeInfo sourceTypeInfo = GetRequiredTypeInfo(metadataOptions, registration.SourceType);
            var migrator = new MigratorReference(
                registration.SourceType,
                registration.SourceMetadata,
                sourceTypeInfo,
                registration.Invoker,
                MigratorReference.ResolveElementMetadata(registration.SourceType, sourceTypeInfo, registry),
                MigratorReference.ResolveElementAcceptsNonObjectShapes(registration.SourceType, sourceTypeInfo, registry));

            AddMigratorCandidate(
                migrators,
                targetType,
                new MigratorCandidate(migrator, MigratorCandidateKind.External));
        }

        // Static target-owned migration is preferred
        // over external migrators for deterministic behavior.
        foreach (StaticMigratorContract contract in FindStaticMigratorMethods(targetType))
        {
            Type sourceType = contract.SourceType;
            TypeMetadata sourceMetadata = registry.GetTypeMetadata(sourceType);

            JsonTypeInfo sourceTypeInfo = GetRequiredTypeInfo(metadataOptions, sourceType);
            var migrator = new MigratorReference(
                sourceType,
                sourceMetadata,
                sourceTypeInfo,
                MigratorInvokerFactory.CreateStaticInvoker(sourceType, targetType, contract.Method),
                MigratorReference.ResolveElementMetadata(sourceType, sourceTypeInfo, registry),
                MigratorReference.ResolveElementAcceptsNonObjectShapes(sourceType, sourceTypeInfo, registry));

            AddMigratorCandidate(
                migrators,
                targetType,
                new MigratorCandidate(migrator, MigratorCandidateKind.Static));
        }

        return [.. migrators.Values.Select(static candidate => candidate.Migrator)];
    }

    private static void ValidateTargetMigratorContracts(Type targetType)
    {
        foreach (Type @interface in targetType.GetInterfaces())
        {
            if (!@interface.IsGenericType
                || @interface.ContainsGenericParameters
                || @interface.GetGenericTypeDefinition() != typeof(IMigrate<,>))
            {
                continue;
            }

            throw new JsonMigrationInvalidTargetMigratorException(targetType, @interface);
        }
    }

    private static MigratorReference? ResolveUndiscriminatedSourceMigrator(
        Type targetType,
        TypeMetadata targetMetadata,
        MigratorReference[] migrators)
    {
        Type? sourceType = targetMetadata.UndiscriminatedSourceType;
        if (sourceType is null)
        {
            return null;
        }

        MigratorReference? match = migrators.FirstOrDefault(migrator => migrator.SourceType == sourceType);
        if (match is not null)
        {
            return match;
        }

        throw new InvalidOperationException(
            $"Target type '{targetType.FullName}' configures '{nameof(JsonMigratableAttribute.UndiscriminatedSourceType)}' as '{sourceType.FullName}', but no migrator was found for '{sourceType.FullName}' -> '{targetType.FullName}'.");
    }

    private static void AddMigratorCandidate(
        Dictionary<string, MigratorCandidate> migrators,
        Type targetType,
        MigratorCandidate candidate)
    {
        string discriminator = candidate.Migrator.SourceMetadata.Discriminator;

        if (!migrators.TryGetValue(discriminator, out MigratorCandidate existing))
        {
            migrators.Add(discriminator, candidate);
            return;
        }

        // Preserve existing behavior: a target-owned static migrator wins over an external migrator
        // for the same source type.
        if (existing.Kind == MigratorCandidateKind.External
            && candidate.Kind == MigratorCandidateKind.Static
            && existing.Migrator.SourceType == candidate.Migrator.SourceType)
        {
            migrators[discriminator] = candidate;
            return;
        }

        throw new JsonMigrationDuplicateTypeDiscriminatorException(
            targetType,
            discriminator,
            existing.Migrator.SourceType,
            candidate.Migrator.SourceType);
    }

    private static IEnumerable<StaticMigratorContract> FindStaticMigratorMethods(Type targetType)
    {
        foreach (Type sourceType in StaticMigratorContracts.GetSourceTypes(targetType))
        {
            MethodInfo method = ResolveStaticTryMigrateMethod(targetType, sourceType);

            yield return new StaticMigratorContract(sourceType, method);
        }
    }

    private static MethodInfo ResolveStaticTryMigrateMethod(Type targetType, Type sourceType)
    {
        MethodInfo[] candidates = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static method => method.Name.Equals(TryMigrateFromMethodName, StringComparison.Ordinal)
                || method.Name.EndsWith($".{TryMigrateFromMethodName}", StringComparison.Ordinal))
            .Where(static method => method.ReturnType == typeof(bool))
            .Where(method =>
            {
                if (method.GetParameters() is not [{ ParameterType: { } firstParameterType }, { IsOut: true } resultParameter])
                {
                    return false;
                }

                if (firstParameterType != sourceType)
                {
                    return false;
                }

                Type? outType = resultParameter.ParameterType.IsByRef
                    ? resultParameter.ParameterType.GetElementType()
                    : resultParameter.ParameterType;

                return outType == targetType;
            })
            .ToArray();

        // Explicit static interface implementations have fully qualified method names,
        // so prefer those when both explicit and public shape matches exist.
        MethodInfo? explicitContractMethod = candidates.FirstOrDefault(IsExplicitContractImplementation);
        return explicitContractMethod
            ?? candidates.First(static method => method.Name.Equals(TryMigrateFromMethodName, StringComparison.Ordinal));
    }

    private static bool IsExplicitContractImplementation(MethodInfo method)
    {
        return method.Name.EndsWith($".{TryMigrateFromMethodName}", StringComparison.Ordinal)
            && method.Name.Contains("IMigrateFrom<", StringComparison.Ordinal);
    }

    private static JsonTypeInfo GetRequiredTypeInfo(JsonSerializerOptions options, Type type)
    {
        try
        {
            return options.GetTypeInfo(type);
        }
        catch (NotSupportedException exception) when (exception is not JsonMigratableTargetKindNotSupportedException)
        {
            throw new InvalidOperationException(
                $"No JSON metadata is available for '{type.FullName}'. Add the type to your JsonSerializerContext or include a resolver that can provide metadata.",
                exception);
        }
    }

    private static void AddDiscriminatorProperty(JsonTypeInfo typeInfo, TypeMetadata metadata)
    {
        // Properties can only be added to object contracts. Unions (.NET 11), collections and
        // dictionaries would otherwise fail inside STJ with a generic "invalid operation for kind"
        // error that does not tell the user where to put the attribute instead.
        if (typeInfo.Kind is not JsonTypeInfoKind.Object)
        {
            throw new JsonMigratableTargetKindNotSupportedException(typeInfo.Type, typeInfo.Kind);
        }

        if (typeInfo.Properties.Any(property => property.Name.Equals(metadata.DiscriminatorPropertyName, StringComparison.Ordinal)))
        {
            return;
        }

        JsonPropertyInfo discriminatorProperty = typeInfo.CreateJsonPropertyInfo(typeof(string), metadata.DiscriminatorPropertyName);
        discriminatorProperty.Order = int.MinValue;
        discriminatorProperty.IsRequired = false;
        discriminatorProperty.Get = _ => metadata.Discriminator;

        typeInfo.Properties.Insert(0, discriminatorProperty);
    }

    private readonly record struct MigratorCandidate(MigratorReference Migrator, MigratorCandidateKind Kind);

    private enum MigratorCandidateKind
    {
        External,
        Static,
    }

    private readonly record struct StaticMigratorContract(Type SourceType, MethodInfo Method);
}
