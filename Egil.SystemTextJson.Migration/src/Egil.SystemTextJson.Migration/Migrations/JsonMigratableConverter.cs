using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class JsonMigratableConverter<T>(MigratorContext context) : JsonConverter<T>
{
    private readonly JsonTypeInfo<T>? targetTypeInfo = context.TargetTypeInfo as JsonTypeInfo<T>;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        InspectionResult inspection = Inspect(ref reader, out string? sourceDiscriminator);

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
        if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
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