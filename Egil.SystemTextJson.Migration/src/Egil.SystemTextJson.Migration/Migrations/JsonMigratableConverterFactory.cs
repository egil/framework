using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Creates migration-aware converters for types annotated with <see cref="JsonMigratableAttribute"/>.
/// </summary>
internal sealed class JsonMigratableConverterFactory(JsonMigrationRegistry registry) : JsonConverterFactory
{
    private const string TryMigrateFromMethodName = nameof(IMigrateFrom<,>.TryMigrateFrom);

    private readonly JsonMigrationRegistry registry = registry;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.GetCustomAttribute<JsonMigratableAttribute>(inherit: true) is not null;

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => CreateConverterCore(typeToConvert, options);

    private JsonConverter CreateConverterCore(Type typeToConvert, JsonSerializerOptions options)
    {
        TypeMetadata targetMetadata = registry.GetTypeMetadata(typeToConvert);

        // Clone options and remove this converter so type metadata lookup does not recursively resolve back to us.
        var metadataOptions = new JsonSerializerOptions(options);
        metadataOptions.Converters.Remove(this);

        JsonTypeInfo targetTypeInfo = GetRequiredTypeInfo(metadataOptions, typeToConvert);
        AddDiscriminatorProperty(targetTypeInfo, targetMetadata);

        var migrators = BuildMigratorMap(typeToConvert, metadataOptions);
        var migratorsByDiscriminator = migrators.ToFrozenDictionary(
            static migrator => migrator.SourceMetadata.Discriminator,
            StringComparer.Ordinal);

        var sourcePropertyNames = migrators
            .Select(static migrator => migrator.SourceMetadata.DiscriminatorPropertyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var context = new MigratorContext(
            targetTypeInfo,
            targetMetadata,
            migratorsByDiscriminator,
            sourcePropertyNames);

        Type converterType = typeof(JsonMigratableConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType, context)!;
    }

    private MigratorReference[] BuildMigratorMap(Type targetType, JsonSerializerOptions metadataOptions)
    {
        var migrators = new Dictionary<string, MigratorCandidate>(StringComparer.Ordinal);

        foreach (ExternalMigratorRegistration registration in registry.GetForTarget(targetType))
        {
            var migrator = new MigratorReference(
                registration.SourceType,
                registration.SourceMetadata,
                GetRequiredTypeInfo(metadataOptions, registration.SourceType),
                registration.Invoker);

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

            var migrator = new MigratorReference(
                sourceType,
                sourceMetadata,
                GetRequiredTypeInfo(metadataOptions, sourceType),
                MigratorInvokerFactory.CreateStaticInvoker(sourceType, targetType, contract.Method));

            AddMigratorCandidate(
                migrators,
                targetType,
                new MigratorCandidate(migrator, MigratorCandidateKind.Static));
        }

        return [.. migrators.Values.Select(static candidate => candidate.Migrator)];
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
        foreach (Type @interface in targetType.GetInterfaces())
        {
            if (!@interface.IsGenericType
                || @interface.ContainsGenericParameters
                || @interface.GetGenericTypeDefinition() != typeof(IMigrateFrom<,>))
            {
                continue;
            }

            Type[] genericArguments = @interface.GetGenericArguments();
            Type sourceType = genericArguments[0];
            Type contractTargetType = genericArguments[1];

            if (contractTargetType != targetType)
            {
                continue;
            }

            MethodInfo method = ResolveStaticTryMigrateMethod(targetType, sourceType)
                ?? throw new InvalidOperationException(
                    $"Type '{targetType.FullName}' declares IMigrateFrom<{sourceType.FullName}, {targetType.FullName}> but no matching static TryMigrateFrom method was found.");

            yield return new StaticMigratorContract(sourceType, method);
        }
    }

    private static MethodInfo? ResolveStaticTryMigrateMethod(Type targetType, Type sourceType)
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

        if (candidates.Length == 0)
        {
            return null;
        }

        // Explicit static interface implementations have fully qualified method names,
        // so prefer those when both explicit and public shape matches exist.
        MethodInfo? explicitContractMethod = candidates.FirstOrDefault(IsExplicitContractImplementation);

        if (explicitContractMethod is not null)
        {
            return explicitContractMethod;
        }

        return candidates.FirstOrDefault(static method => method.Name.Equals(TryMigrateFromMethodName, StringComparison.Ordinal));
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
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(
                $"No JSON metadata is available for '{type.FullName}'. Add the type to your JsonSerializerContext or include a resolver that can provide metadata.",
                exception);
        }
    }

    private static void AddDiscriminatorProperty(JsonTypeInfo typeInfo, TypeMetadata metadata)
    {
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
