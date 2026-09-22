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
    private DefaultJsonTypeInfoResolver? reflectionFallback;

    /// <param name="precedingConverters">
    /// The converters already present in <see cref="JsonSerializerOptions.Converters"/> when migration
    /// support was added. STJ picks the first matching converter from that list, and before this
    /// resolver existed the migration converter factory sat at the position where it was registered, so
    /// a converter registered earlier won over migration and one registered later lost. The resolver
    /// keeps that order by stepping aside for the earlier ones while they remain registered.
    /// </param>
    public JsonMigrationTypeInfoResolver(JsonMigrationRegistry registry, IEnumerable<JsonConverter> precedingConverters)
    {
        this.registry = registry;
        this.precedingConverters = [.. precedingConverters];
    }

    internal JsonMigrationRegistry Registry => registry;

    /// <inheritdoc/>
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        if (type == typeof(ResolverProbe.Marker))
        {
            return ResolverProbe.Answer(this, options);
        }

        // The scope describes the options being resolved (which types are excluded, which options
        // the user configured), so it is honoured whichever migration resolver registered it. Two
        // migration resolvers can be active at once when the options were registered again after
        // an opaque wrapper hid the first one; had each rejected the other's exclusions, a type's
        // converter would build itself again without end.
        MigrationScope? scope = MigrationScope.Find(options);

        // The type whose converter is being built through these options wants its plain object
        // contract. When a downstream resolver exists, stepping aside lets it supply the contract;
        // when this resolver stands in for the default, the reflection contract is produced here
        // rather than in the clone's wrapper, so that a decorator around this entry applies its
        // modifiers to the plain contract of a migratable type exactly as to any other contract.
        if (scope is not null && scope.IsBuildingConverterFor(type))
        {
            return ResolveReflectionFallback(type, options, scope);
        }

        if (JsonMigratableTypes.IsMigratable(type) && !HasPrecedingConverter(type, options))
        {
            return CreateMigrationTypeInfo(type, options, scope);
        }

        return ResolveReflectionFallback(type, options, scope);
    }

    private bool HasPrecedingConverter(Type type, JsonSerializerOptions options)
    {
        // A converter registered before migration support only keeps precedence while it is still
        // in the options being resolved: a copy that dropped it must let migration serve the type,
        // as the converter factory it replaced would have.
        foreach (JsonConverter converter in precedingConverters)
        {
            if (options.Converters.Contains(converter) && converter.CanConvert(type))
            {
                return true;
            }
        }

        return false;
    }

    internal JsonTypeInfo? ResolveReflectionFallback(Type type, JsonSerializerOptions options, MigrationScope? scope)
    {
        // See MigrationScope.UsesReflectionFallback for when the resolver stands in for the default.
        // A copy of registered options has no scope of its own; its chain is inspected directly.
        bool applies = scope?.UsesReflectionFallback ?? new MigrationScope(this, options, []).UsesReflectionFallback;
        if (!applies || !JsonSerializer.IsReflectionEnabledByDefault)
        {
            return null;
        }

        reflectionFallback ??= new DefaultJsonTypeInfoResolver();
        return reflectionFallback.GetTypeInfo(type, options);
    }

    private JsonTypeInfo CreateMigrationTypeInfo(Type typeToConvert, JsonSerializerOptions options, MigrationScope? scope)
    {
        ValidateTargetMigratorContracts(typeToConvert);

        TypeMetadata targetMetadata = registry.GetTypeMetadata(typeToConvert);

        // Clone the options so metadata lookup still applies migration converters for nested
        // migratable types while the type itself resolves to its plain object contract. The clone
        // keeps the inherited resolver chain untouched (including any decorator the user applied)
        // and wraps it: the scope registered for the clone makes this resolver step aside for the
        // type, and the wrapper adds the discriminator to the contract that comes back.
        // The clone's scope names this resolver so the discriminator metadata added to the plain
        // contract comes from the registry that builds the converter.
        var metadataOptions = new JsonSerializerOptions(options);
        MigrationScope cloneScope = (scope?.ForResolver(this) ?? new MigrationScope(this, options, [])).CreateExcluding(typeToConvert);
        MigrationScope.Register(metadataOptions, cloneScope);
        metadataOptions.TypeInfoResolver = new PlainContractResolver(metadataOptions.TypeInfoResolver!, cloneScope);

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

    internal static void AddDiscriminatorProperty(JsonTypeInfo typeInfo, TypeMetadata metadata)
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
