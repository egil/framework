using System.Collections.Concurrent;
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
    private readonly JsonMigrationRegistry registry = registry;
    private readonly ConcurrentDictionary<Type, JsonConverter> converterCache = new();

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.GetCustomAttribute<JsonMigratableAttribute>(inherit: true) is not null;

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => converterCache.GetOrAdd(typeToConvert, _ => CreateConverterCore(typeToConvert, options));

    private JsonConverter CreateConverterCore(Type typeToConvert, JsonSerializerOptions options)
    {
        TypeMetadata targetMetadata = TypeMetadata.FromType(typeToConvert);

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
        var migrators = new Dictionary<string, MigratorReference>(StringComparer.Ordinal);

        foreach (ExternalMigratorRegistration registration in registry.GetForTarget(targetType))
        {
            migrators[registration.SourceMetadata.Discriminator] = new MigratorReference(
                registration.SourceType,
                registration.SourceMetadata,
                GetRequiredTypeInfo(metadataOptions, registration.SourceType),
                registration.Invoker);
        }

        // Static target-owned migration is preferred
        // over external migrators for deterministic behavior.
        foreach (MethodInfo method in FindStaticMigratorMethods(targetType))
        {
            Type sourceType = method.GetParameters()[0].ParameterType;
            TypeMetadata sourceMetadata = TypeMetadata.FromType(sourceType);

            migrators[sourceMetadata.Discriminator] = new MigratorReference(
                sourceType,
                sourceMetadata,
                GetRequiredTypeInfo(metadataOptions, sourceType),
                MigratorInvokerFactory.CreateStaticInvoker(sourceType, targetType, method));
        }

        return [.. migrators.Values];
    }

    private static IEnumerable<MethodInfo> FindStaticMigratorMethods(Type targetType)
    {
        return targetType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static method => method.Name.Equals("TryMigrateFrom", StringComparison.Ordinal))
            .Where(static method => method.ReturnType == typeof(bool))
            .Where(method =>
            {
                if (method.GetParameters() is not [{ ParameterType: { } }, { IsOut: true } resultParameter])
                {
                    return false;
                }

                Type? outType = resultParameter.ParameterType.IsByRef
                    ? resultParameter.ParameterType.GetElementType()
                    : resultParameter.ParameterType;

                return outType == targetType;
            });
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
}
