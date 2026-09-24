using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// The non-generic view of <see cref="JsonMigratableConverter{T}"/>, for callers that hold a
/// contract's converter and need to know which migration resolver built it.
/// </summary>
internal interface IJsonMigratableConverter
{
    JsonMigrationTypeInfoResolver Resolver { get; }
}

internal sealed partial class JsonMigratableConverter<T>(MigratorContext context) : JsonConverter<T>, IJsonMigratableConverter
{
    public JsonMigrationTypeInfoResolver Resolver => context.Resolver;

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
            return SetMigrationTracking(legacy, migratedDuringDeserialization: true);
        }

        if (inspection is InspectionResult.TargetType)
        {
            T? current = DeserializeTarget(ref reader, typeToConvert);
            return SetMigrationTracking(current, migratedDuringDeserialization: false);
        }

        Debug.Assert(migrator is not null);

        var sourceReader = reader;
        if (!((MigratorReference<T>)migrator).TryRead(ref reader, out T? typedMigrated))
        {
            JsonMigrationMeter.RecordMigration(migrator.SourceTypeName, targetTypeName, success: false);

            if (context.MigrationFailureHandling is JsonMigrationFailureHandling.FallBackToTargetType)
            {
                T? fallback = DeserializeTarget(ref sourceReader, typeToConvert);
                return SetMigrationTracking(fallback, migratedDuringDeserialization: false);
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
        return SetMigrationTracking(typedMigrated, migratedDuringDeserialization: true);
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
            // Non-object payloads (arrays, primitives) have no discriminator property.
            // Try to find a registered migrator whose source type is compatible with
            // the JSON token type based on its JsonTypeInfoKind.
            migrator = FindMigratorForNonObjectPayload(ref probe, probe.TokenType);
            return migrator is not null
                ? InspectionResult.MigrationRequired
                : InspectionResult.LegacyPayload;
        }

        if (!probe.Read())
        {
            throw new JsonException("Unexpected end of JSON payload.");
        }

        if (probe.TokenType is JsonTokenType.EndObject)
        {
            return InspectUndiscriminatedObject(out migrator);
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
                EnsureUnambiguousDiscriminator(probe, selected: null);
                return InspectionResult.TargetType;
            }

            // Zero-allocation: match discriminator directly against known migrators
            // using pre-encoded UTF-8 bytes instead of allocating a string.
            migrator = FindMigratorByDiscriminator(ref probe, context.TargetDiscriminatorPropertyNameUtf8);
            if (migrator is not null)
            {
                EnsureUnambiguousDiscriminator(probe, migrator);
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

            // Match both the property and its value; different source contracts can reuse a value.
            migrator = FindMigratorByDiscriminator(ref probe, sourcePropertyName);
            if (migrator is not null)
            {
                EnsureUnambiguousDiscriminator(probe, migrator);
                return InspectionResult.MigrationRequired;
            }

            // Slow path: allocate string only for validation/error reporting.
            ThrowUnknownDiscriminator(ref probe);
        }

        InspectionResult undiscriminatedObjectResult = InspectUndiscriminatedObject(out migrator);
        if (undiscriminatedObjectResult is InspectionResult.MigrationRequired)
        {
            return undiscriminatedObjectResult;
        }

        // The first property didn't match any discriminator. Before treating
        // as a legacy payload, check for dictionary-kind migrators — dictionaries
        // serialize as JSON objects but have no discriminator.
        migrator = FindMigratorForDictionaryPayload(ref probe);
        if (migrator is not null)
        {
            return InspectionResult.MigrationRequired;
        }

        return InspectionResult.LegacyPayload;
    }

    private InspectionResult InspectUndiscriminatedObject(out MigratorReference? migrator)
    {
        migrator = context.UndiscriminatedSourceMigrator;
        return migrator is not null
            ? InspectionResult.MigrationRequired
            : InspectionResult.LegacyPayload;
    }

    private MigratorReference? FindMigratorByDiscriminator(ref Utf8JsonReader reader, byte[] propertyName)
    {
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (propertyName.AsSpan().SequenceEqual(migrator.DiscriminatorPropertyNameUtf8)
                && reader.ValueTextEquals(migrator.DiscriminatorUtf8))
            {
                return migrator;
            }
        }

        return null;
    }

    private void EnsureUnambiguousDiscriminator(Utf8JsonReader probe, MigratorReference? selected)
    {
        // Only layouts with distinct discriminator properties require a second pass. Single-property layouts keep their
        // first-property dispatch and avoid scanning the entire object twice.
        if (!context.HasDistinctDiscriminatorProperties)
        {
            return;
        }

        while (probe.Read() && probe.TokenType is not JsonTokenType.EndObject)
        {
            if (probe.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected '{JsonTokenType.PropertyName}', got '{probe.TokenType}'.");
            }

            if (selected is not null && probe.ValueTextEquals(context.TargetDiscriminatorPropertyNameUtf8)
                && HasDiscriminatorValue(probe, context.TargetDiscriminatorUtf8))
            {
                throw new JsonException("Multiple discriminator properties matched target or source contracts.");
            }

            foreach (MigratorReference candidate in context.Migrators)
            {
                if (candidate != selected && probe.ValueTextEquals(candidate.DiscriminatorPropertyNameUtf8))
                {
                    if (HasDiscriminatorValue(probe, candidate.DiscriminatorUtf8))
                    {
                        throw new JsonException("Multiple discriminator properties matched target or source contracts.");
                    }
                }
            }

            if (!probe.Read())
            {
                throw new JsonException("Unexpected end of JSON payload.");
            }

            probe.Skip();
        }
    }

    private static bool HasDiscriminatorValue(Utf8JsonReader probe, byte[] discriminator) =>
        probe.Read() && probe.TokenType is JsonTokenType.String && probe.ValueTextEquals(discriminator);

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
    private static T? SetMigrationTracking(T? value, bool migratedDuringDeserialization)
    {
        // The interface keeps tracking opt-in so regular domain models stay free of migration concerns.
        if (value is IJsonMigrationTracked tracked)
        {
            tracked.MigratedDuringDeserialization = migratedDuringDeserialization;

            // A struct matches the pattern as a boxed copy, so the flag was set on that copy;
            // return it instead of the unchanged original.
            if (typeof(T).IsValueType)
            {
                return (T)tracked;
            }
        }

        return value;
    }

    private enum InspectionResult
    {
        TargetType,
        MigrationRequired,
        LegacyPayload,
    }
}
