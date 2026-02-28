using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

public class RegistrationAndTrackingTests
{
    [Fact]
    public void Tracking_is_true_when_migrated()
    {
        var options = CreateOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var json = JsonSerializer.Serialize(new TrackingV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<TrackingV3>(json, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Tracking_is_true_when_discriminator_is_missing()
    {
        var options = CreateOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var migrated = JsonSerializer.Deserialize<TrackingV3>("""
            {"firstName":"Egil","lastName":"Hansen","age":42}
            """, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Tracking_is_false_when_payload_matches_target_type()
    {
        var options = CreateOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var json = JsonSerializer.Serialize(new TrackingV3("Egil", "Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<TrackingV3>(json, options);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.MigratedDuringDeserialization);
    }

    [Fact]
    public void Static_migrator_takes_precedence_over_external_migrator()
    {
        var options = CreateOptions(static builder => builder.RegisterMigrator<PrecedenceExternalMigrator>());

        var json = JsonSerializer.Serialize(new PrecedenceV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<PrecedenceV2>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("static", migrated.MigrationPath);
    }

    [Fact]
    public void Scoped_assembly_scan_registers_external_migrator()
    {
        var options = CreateOptions(builder => builder.RegisterMigratorsFromAssembly(typeof(TrackingExternalMigrator).Assembly));

        var json = JsonSerializer.Serialize(new ScanSource("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<ScanTarget>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.Equal("Hansen", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Missing_registration_throws_for_known_source_discriminator()
    {
        var options = CreateOptions();

        var json = JsonSerializer.Serialize(new ScanSource("Egil Hansen", 42), options);

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScanTarget>(json, options));
        Assert.Contains("No migrator was found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_registration_for_same_pair_throws()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.AddJsonMigrationSupport(builder =>
            {
                builder.RegisterMigrator<TrackingV1, TrackingV3, TrackingExternalMigrator>();
                builder.RegisterMigrator<TrackingV1, TrackingV3, TrackingExternalMigrator>();
            }));

        Assert.Contains("Duplicate migrator registration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_attribute_discriminator_is_used_when_configured()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder
            .GetTypeDiscriminatorFrom<MigrationAliasAttribute>(static attribute => attribute.Alias)
            .RegisterMigrator<AliasPreferredMigrator>());

        var migrated = JsonSerializer.Deserialize<AliasPreferredTarget>(
            """
            {"$type":"alias-source-v1","name":"Egil Hansen","age":42}
            """,
            options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.Equal("Hansen", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Json_migratable_discriminator_is_used_as_fallback_when_custom_attribute_is_missing()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder
            .GetTypeDiscriminatorFrom<MigrationAliasAttribute>(static attribute => attribute.Alias)
            .RegisterMigrator<AliasFallbackMigrator>());

        var migrated = JsonSerializer.Deserialize<AliasFallbackTarget>(
            """
            {"$type":"json-fallback-source-v1","name":"Egil Hansen","age":42}
            """,
            options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.Equal("Hansen", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Source_generated_context_only_works_when_metadata_is_complete()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);

        var json = JsonSerializer.Serialize(new TrackingV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<TrackingV3>(json, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Source_generated_context_only_fails_with_clear_error_when_metadata_is_missing()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<MissingMetadataMigrator>());
        options.TypeInfoResolverChain.Add(MissingMetadataJsonContext.Default);

        var payload = """
            {"$type":"Egil.SystemTextJson.Migration.Tests.MissingMetadataSource","name":"Egil Hansen","age":42}
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => JsonSerializer.Deserialize<MissingMetadataTarget>(payload, options));

        Assert.Contains("No JSON metadata is available", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Copied_options_use_their_own_metadata_context_for_converter_creation()
    {
        const string payload = """
            {"$type":"Egil.SystemTextJson.Migration.Tests.MissingMetadataSource","name":"Egil Hansen","age":42}
            """;

        var optionsA = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        optionsA.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<MissingMetadataMigrator>());
        optionsA.TypeInfoResolverChain.Add(CompleteMissingMetadataJsonContext.Default);

        var migrated = JsonSerializer.Deserialize<MissingMetadataTarget>(payload, optionsA);
        Assert.NotNull(migrated);

        var optionsB = new JsonSerializerOptions(optionsA);
        optionsB.TypeInfoResolverChain.Clear();
        optionsB.TypeInfoResolverChain.Add(MissingMetadataJsonContext.Default);

        var exception = Assert.Throws<InvalidOperationException>(
            () => JsonSerializer.Deserialize<MissingMetadataTarget>(payload, optionsB));

        Assert.Contains("No JSON metadata is available", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_lifecycle_callbacks_are_invoked_for_migratable_target_type()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var value = new CallbackLifecycleTarget
        {
            FirstName = "Egil",
            LastName = "Hansen",
            Age = 42,
        };

        var json = JsonSerializer.Serialize(value, options);

        Assert.True(value.OnSerializingCalled);
        Assert.True(value.OnSerializedCalled);

        var deserialized = JsonSerializer.Deserialize<CallbackLifecycleTarget>(json, options);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.OnDeserializingCalled);
        Assert.True(deserialized.OnDeserializedCalled);
    }

    private static JsonSerializerOptions CreateOptions(Action<JsonMigrationBuilder>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(configure);
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);
        return options;
    }
}

[JsonMigratable]
public record class TrackingV1(string Name, int Age);

[JsonMigratable]
public record class TrackingV3(string FirstName, string LastName, int Age) : IJsonMigrationTracked
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }
}

public class TrackingExternalMigrator :
    IMigrate<TrackingV1, TrackingV3>
{
    public bool TryMigrateFrom(TrackingV1 source, out TrackingV3 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new TrackingV3(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[JsonMigratable]
public record class PrecedenceV1(string Name, int Age);

[JsonMigratable]
public record class PrecedenceV2(string FirstName, string LastName, int Age, string MigrationPath) :
    IMigrateFrom<PrecedenceV1, PrecedenceV2>
{
    public static bool TryMigrateFrom(PrecedenceV1 source, out PrecedenceV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new PrecedenceV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            "static");
        return true;
    }
}

public class PrecedenceExternalMigrator : IMigrate<PrecedenceV1, PrecedenceV2>
{
    public bool TryMigrateFrom(PrecedenceV1 source, out PrecedenceV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new PrecedenceV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            "external");
        return true;
    }
}

[JsonMigratable]
public record class ScanSource(string Name, int Age);

[JsonMigratable]
public record class ScanTarget(string FirstName, string LastName, int Age);

public class ScanMigrator : IMigrate<ScanSource, ScanTarget>
{
    public bool TryMigrateFrom(ScanSource source, out ScanTarget result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new ScanTarget(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class MigrationAliasAttribute(string alias) : Attribute
{
    public string Alias { get; } = alias;
}

[MigrationAlias("alias-source-v1")]
[JsonMigratable(TypeDiscriminator = "json-alias-source-v1")]
public record class AliasPreferredSource(string Name, int Age);

[JsonMigratable]
public record class AliasPreferredTarget(string FirstName, string LastName, int Age);

public class AliasPreferredMigrator : IMigrate<AliasPreferredSource, AliasPreferredTarget>
{
    public bool TryMigrateFrom(AliasPreferredSource source, out AliasPreferredTarget result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new AliasPreferredTarget(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "json-fallback-source-v1")]
public record class AliasFallbackSource(string Name, int Age);

[JsonMigratable]
public record class AliasFallbackTarget(string FirstName, string LastName, int Age);

public class AliasFallbackMigrator : IMigrate<AliasFallbackSource, AliasFallbackTarget>
{
    public bool TryMigrateFrom(AliasFallbackSource source, out AliasFallbackTarget result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new AliasFallbackTarget(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[JsonMigratable]
public record class MissingMetadataSource(string Name, int Age);

[JsonMigratable]
public record class MissingMetadataTarget(string FirstName, string LastName, int Age);

public class MissingMetadataMigrator : IMigrate<MissingMetadataSource, MissingMetadataTarget>
{
    public bool TryMigrateFrom(MissingMetadataSource source, out MissingMetadataTarget result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new MissingMetadataTarget(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[JsonMigratable]
public sealed class CallbackLifecycleTarget :
    IJsonOnSerializing,
    IJsonOnSerialized,
    IJsonOnDeserializing,
    IJsonOnDeserialized
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public int Age { get; set; }

    [JsonIgnore]
    public bool OnSerializingCalled { get; private set; }

    [JsonIgnore]
    public bool OnSerializedCalled { get; private set; }

    [JsonIgnore]
    public bool OnDeserializingCalled { get; private set; }

    [JsonIgnore]
    public bool OnDeserializedCalled { get; private set; }

    public void OnSerializing()
        => OnSerializingCalled = true;

    public void OnSerialized()
        => OnSerializedCalled = true;

    public void OnDeserializing()
        => OnDeserializingCalled = true;

    public void OnDeserialized()
        => OnDeserializedCalled = true;
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(TrackingV1))]
[JsonSerializable(typeof(TrackingV3))]
[JsonSerializable(typeof(PrecedenceV1))]
[JsonSerializable(typeof(PrecedenceV2))]
[JsonSerializable(typeof(ScanSource))]
[JsonSerializable(typeof(ScanTarget))]
public partial class TrackingJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(MissingMetadataTarget))]
public partial class MissingMetadataJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(MissingMetadataSource))]
[JsonSerializable(typeof(MissingMetadataTarget))]
public partial class CompleteMissingMetadataJsonContext : JsonSerializerContext;
