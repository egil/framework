using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration;

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

        // Static target-owned migration is preferred over external migrators for deterministic behavior.
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

internal sealed class JsonMigratableConverter<T>(MigratorContext context) : JsonConverter<T>
{
    private readonly JsonTypeInfo<T>? targetTypeInfo = context.TargetTypeInfo as JsonTypeInfo<T>;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        InspectionResult inspection = Inspect(ref reader, out string? sourceDiscriminator);

        if (inspection == InspectionResult.LegacyPayload)
        {
            T? legacy = DeserializeTarget(ref reader, typeToConvert);
            SetMigrationTracking(legacy, migratedDuringDeserialization: true);
            return legacy;
        }

        if (inspection == InspectionResult.TargetType)
        {
            T? current = DeserializeTarget(ref reader, typeToConvert);
            SetMigrationTracking(current, migratedDuringDeserialization: false);
            return current;
        }

        if (sourceDiscriminator is null || !context.MigratorsByDiscriminator.TryGetValue(sourceDiscriminator, out MigratorReference? migrator))
        {
            throw new JsonException($"No migrator was found for discriminator '{sourceDiscriminator}'.");
        }

        object? source = JsonSerializer.Deserialize(ref reader, migrator.SourceTypeInfo);
        if (!migrator.Invoker.TryMigrate(source, out object? migrated) || migrated is not T typedMigrated)
        {
            throw new JsonException($"Migration failed for '{migrator.SourceType.FullName}' -> '{typeof(T).FullName}'.");
        }

        SetMigrationTracking(typedMigrated, migratedDuringDeserialization: true);
        return typedMigrated;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (targetTypeInfo is not null)
        {
            JsonSerializer.Serialize(writer, value, targetTypeInfo);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, context.TargetTypeInfo);
        }
    }

    private T? DeserializeTarget(ref Utf8JsonReader reader, Type typeToConvert)
    {
        if (targetTypeInfo is not null)
        {
            return JsonSerializer.Deserialize(ref reader, targetTypeInfo);
        }

        return (T?)JsonSerializer.Deserialize(ref reader, context.TargetTypeInfo);
    }

    private InspectionResult Inspect(ref Utf8JsonReader reader, out string? sourceDiscriminator)
    {
        sourceDiscriminator = null;

        var probe = reader;
        if (probe.TokenType == JsonTokenType.None && !probe.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (probe.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected '{JsonTokenType.StartObject}', got '{probe.TokenType}'.");
        }

        if (!probe.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (probe.TokenType == JsonTokenType.EndObject)
        {
            return InspectionResult.LegacyPayload;
        }

        if (probe.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected '{JsonTokenType.PropertyName}', got '{probe.TokenType}'.");
        }

        if (probe.ValueTextEquals(context.TargetDiscriminatorPropertyNameUtf8))
        {
            sourceDiscriminator = ReadDiscriminatorValue(ref probe);
            return sourceDiscriminator != null
                && sourceDiscriminator.Equals(context.TargetMetadata.Discriminator, StringComparison.Ordinal)
                ? InspectionResult.TargetType
                : InspectionResult.MigrationRequired;
        }

        foreach (byte[] sourcePropertyName in context.SourceDiscriminatorPropertyNameUtf8)
        {
            if (!probe.ValueTextEquals(sourcePropertyName))
            {
                continue;
            }

            sourceDiscriminator = ReadDiscriminatorValue(ref probe);
            return InspectionResult.MigrationRequired;
        }

        return InspectionResult.LegacyPayload;
    }

    private static string? ReadDiscriminatorValue(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected discriminator string, got '{reader.TokenType}'.");
        }

        string? value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Type discriminator cannot be null or empty.");
        }

        return value;
    }

    private static void SetMigrationTracking(T? value, bool migratedDuringDeserialization)
    {
        // The interface keeps tracking opt-in so regular domain models stay free of migration concerns.
        if (value is IJsonMigrationTracked tracked)
        {
            tracked.MigratedDuringDeserialization = migratedDuringDeserialization;
        }
    }

    private enum InspectionResult
    {
        TargetType,
        MigrationRequired,
        LegacyPayload,
    }
}

internal sealed record MigratorContext(
    JsonTypeInfo TargetTypeInfo,
    TypeMetadata TargetMetadata,
    FrozenDictionary<string, MigratorReference> MigratorsByDiscriminator,
    string[] SourceDiscriminatorPropertyNames)
{
    public byte[] TargetDiscriminatorPropertyNameUtf8 { get; } = System.Text.Encoding.UTF8.GetBytes(TargetMetadata.DiscriminatorPropertyName);

    public byte[][] SourceDiscriminatorPropertyNameUtf8 { get; } =
        SourceDiscriminatorPropertyNames
            .Select(System.Text.Encoding.UTF8.GetBytes)
            .ToArray();
}

internal sealed record MigratorReference(
    Type SourceType,
    TypeMetadata SourceMetadata,
    JsonTypeInfo SourceTypeInfo,
    IMigratorInvoker Invoker);

internal sealed record TypeMetadata(
    Type Type,
    string Discriminator,
    string DiscriminatorPropertyName)
{
    public static TypeMetadata FromType(Type type)
    {
        JsonMigratableAttribute? attribute = type.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
        string discriminator = attribute?.TypeDiscriminator ?? type.FullName ?? type.Name;
        string propertyName = attribute?.TypeDiscriminatorPropertyName ?? "$type";
        return new TypeMetadata(type, discriminator, propertyName);
    }
}

internal sealed record ExternalMigratorRegistration(
    Type SourceType,
    Type TargetType,
    TypeMetadata SourceMetadata,
    IMigratorInvoker Invoker);

internal sealed class JsonMigrationRegistry(FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget)
{
    private readonly FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget = registrationsByTarget;

    public IEnumerable<ExternalMigratorRegistration> GetForTarget(Type targetType)
        => registrationsByTarget.TryGetValue(targetType, out FrozenDictionary<Type, ExternalMigratorRegistration>? registrations)
            ? registrations.Values
            : [];
}

internal interface IMigratorInvoker
{
    bool TryMigrate(object? source, out object? migrated);
}

internal static class MigratorInvokerFactory
{
    public static IMigratorInvoker CreateExternalInvoker(Type sourceType, Type targetType, object migrator)
    {
        ArgumentNullException.ThrowIfNull(migrator);

        MethodInfo method = typeof(MigratorInvokerFactory)
            .GetMethod(nameof(CreateExternalInvokerGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(sourceType, targetType);

        return (IMigratorInvoker)method.Invoke(null, [migrator])!;
    }

    public static IMigratorInvoker CreateStaticInvoker(Type sourceType, Type targetType, MethodInfo method)
    {
        MethodInfo factoryMethod = typeof(MigratorInvokerFactory)
            .GetMethod(nameof(CreateStaticInvokerGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(sourceType, targetType);

        return (IMigratorInvoker)factoryMethod.Invoke(null, [method])!;
    }

    private static IMigratorInvoker CreateExternalInvokerGeneric<TSource, TTarget>(object migrator)
        => new ExternalMigratorInvoker<TSource, TTarget>((IMigrate<TSource, TTarget>)migrator);

    private static IMigratorInvoker CreateStaticInvokerGeneric<TSource, TTarget>(MethodInfo method)
    {
        var migrator = (TryMigrateDelegate<TSource, TTarget>)method.CreateDelegate(typeof(TryMigrateDelegate<TSource, TTarget>));
        return new StaticMigratorInvoker<TSource, TTarget>(migrator);
    }
}

internal sealed class ExternalMigratorInvoker<TSource, TTarget>(IMigrate<TSource, TTarget> migrator) : IMigratorInvoker
{
    public bool TryMigrate(object? source, out object? migrated)
    {
        if (source is TSource typed && migrator.TryMigrateFrom(typed, out TTarget? result))
        {
            migrated = result;
            return true;
        }

        migrated = null;
        return false;
    }
}

internal delegate bool TryMigrateDelegate<TSource, TTarget>(TSource source, out TTarget result);

internal sealed class StaticMigratorInvoker<TSource, TTarget>(TryMigrateDelegate<TSource, TTarget> migrator) : IMigratorInvoker
{
    public bool TryMigrate(object? source, out object? migrated)
    {
        if (source is TSource typed && migrator(typed, out TTarget? result))
        {
            migrated = result;
            return true;
        }

        migrated = null;
        return false;
    }
}
