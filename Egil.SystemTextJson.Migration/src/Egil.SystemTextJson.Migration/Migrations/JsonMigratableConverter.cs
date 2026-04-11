using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class JsonMigratableConverter<T>(MigratorContext context) : JsonConverter<T>
{
    private readonly JsonTypeInfo<T>? targetTypeInfo = context.TargetTypeInfo as JsonTypeInfo<T>;

    // Cache the target converter and options to call Read directly, bypassing
    // the GetReaderScopedToNextValue overhead inside JsonSerializer.Deserialize.
    private readonly JsonConverter<T>? targetConverter = context.TargetTypeInfo.Converter as JsonConverter<T>;
    private readonly JsonSerializerOptions targetOptions = context.TargetTypeInfo.Options;

    // Pre-cache the target type name to avoid repeated property access in the telemetry path.
    private readonly string targetTypeName = typeof(T).FullName ?? typeof(T).Name;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        InspectionResult inspection = Inspect(ref reader, out MigratorReference? migrator);

        if (inspection is InspectionResult.LegacyPayload)
        {
            T? legacy = DeserializeTarget(ref reader, typeToConvert);
            SetMigrationTracking(legacy, migratedDuringDeserialization: true);
            return legacy;
        }

        if (inspection is InspectionResult.TargetType)
        {
            T? current = DeserializeTarget(ref reader, typeToConvert);
            SetMigrationTracking(current, migratedDuringDeserialization: false);
            return current;
        }

        Debug.Assert(migrator is not null);

        var sourceReader = reader;
        object? source = StjInternals.ReadAsObject(
            migrator.SourceTypeInfo.Converter,
            ref reader,
            migrator.SourceType,
            migrator.SourceTypeInfo.Options);
        if (!migrator.Invoker.TryMigrate(source, out object? migrated) || migrated is not T typedMigrated)
        {
            JsonMigrationMeter.RecordMigration(migrator.SourceTypeName, targetTypeName, success: false);

            if (context.MigrationFailureHandling is JsonMigrationFailureHandling.FallBackToTargetType)
            {
                T? fallback = DeserializeTarget(ref sourceReader, typeToConvert);
                SetMigrationTracking(fallback, migratedDuringDeserialization: false);
                return fallback;
            }

            if (context.MigrationFailureHandling is JsonMigrationFailureHandling.ReturnNull)
            {
                if (default(T) is null)
                {
                    return default;
                }

                throw new JsonException(
                    $"Migration failed for '{migrator.SourceType.FullName}' -> '{typeof(T).FullName}' and configured handling '{JsonMigrationFailureHandling.ReturnNull}' cannot be applied to non-nullable value type targets.");
            }

            throw new JsonException($"Migration failed for '{migrator.SourceType.FullName}' -> '{typeof(T).FullName}'.");
        }

        JsonMigrationMeter.RecordMigration(migrator.SourceTypeName, targetTypeName, success: true);
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
        // Call the converter's Read method directly instead of JsonSerializer.Deserialize.
        // JsonResumableConverter<T>.Read creates a ReadStack and calls TryRead directly,
        // bypassing the GetReaderScopedToNextValue overhead that copies and skips the
        // entire JSON value before re-parsing it.
        if (targetConverter is not null)
        {
            return targetConverter.Read(ref reader, typeToConvert, targetOptions);
        }

        return (T?)StjInternals.ReadAsObject(
            context.TargetTypeInfo.Converter,
            ref reader,
            typeToConvert,
            targetOptions);
    }

    private InspectionResult Inspect(ref Utf8JsonReader reader, out MigratorReference? migrator)
    {
        migrator = null;

        var probe = reader;
        if (probe.TokenType is JsonTokenType.None && !probe.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (probe.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected '{JsonTokenType.StartObject}', got '{probe.TokenType}'.");
        }

        if (!probe.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (probe.TokenType is JsonTokenType.EndObject)
        {
            return InspectionResult.LegacyPayload;
        }

        if (probe.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected '{JsonTokenType.PropertyName}', got '{probe.TokenType}'.");
        }

        if (probe.ValueTextEquals(context.TargetDiscriminatorPropertyNameUtf8))
        {
            if (!probe.Read() || probe.TokenType is not JsonTokenType.String)
            {
                throw new JsonException($"Expected discriminator string, got '{probe.TokenType}'.");
            }

            // Fast path: compare the discriminator value directly as UTF-8 bytes
            // to avoid a string allocation when the payload matches the target type.
            if (probe.ValueTextEquals(context.TargetDiscriminatorUtf8))
            {
                return InspectionResult.TargetType;
            }

            // Zero-allocation: match discriminator directly against known migrators
            // using pre-encoded UTF-8 bytes instead of allocating a string.
            migrator = FindMigratorByDiscriminator(ref probe);
            if (migrator is not null)
            {
                return InspectionResult.MigrationRequired;
            }

            // Slow path: allocate string only for validation/error reporting.
            ThrowUnknownDiscriminator(ref probe);
        }

        foreach (byte[] sourcePropertyName in context.SourceDiscriminatorPropertyNameUtf8)
        {
            if (!probe.ValueTextEquals(sourcePropertyName))
            {
                continue;
            }

            if (!probe.Read() || probe.TokenType is not JsonTokenType.String)
            {
                throw new JsonException($"Expected discriminator string, got '{probe.TokenType}'.");
            }

            // Zero-allocation: match discriminator directly against known migrators.
            migrator = FindMigratorByDiscriminator(ref probe);
            if (migrator is not null)
            {
                return InspectionResult.MigrationRequired;
            }

            // Slow path: allocate string only for validation/error reporting.
            ThrowUnknownDiscriminator(ref probe);
        }

        return InspectionResult.LegacyPayload;
    }

    private MigratorReference? FindMigratorByDiscriminator(ref Utf8JsonReader reader)
    {
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (reader.ValueTextEquals(migrator.DiscriminatorUtf8))
            {
                return migrator;
            }
        }

        return null;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUnknownDiscriminator(ref Utf8JsonReader reader)
    {
        string? sourceDiscriminator = reader.GetString();
        if (string.IsNullOrWhiteSpace(sourceDiscriminator))
        {
            throw new JsonException("Type discriminator cannot be null or empty.");
        }

        throw new JsonException($"No migrator was found for discriminator '{sourceDiscriminator}'.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
