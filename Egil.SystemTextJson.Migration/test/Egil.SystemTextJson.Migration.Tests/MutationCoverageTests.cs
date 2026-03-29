using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Egil.SystemTextJson.Migration.Tests;

public class MutationCoverageTests
{
    [Fact]
    public void Deserialize_throws_when_payload_starts_with_non_object_token()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackingV3>("[]", options));

        Assert.Contains("Expected 'StartObject'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_throws_when_discriminator_value_is_not_string()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        const string payload = """
            {"$type":42,"firstName":"Egil","lastName":"Hansen","age":42}
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackingV3>(payload, options));

        Assert.Contains("Expected discriminator string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_throws_when_discriminator_value_is_whitespace()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        const string payload = """
            {"$type":"   ","firstName":"Egil","lastName":"Hansen","age":42}
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackingV3>(payload, options));

        Assert.Contains("Type discriminator cannot be null or empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_empty_object_treats_payload_as_legacy_and_sets_tracking()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var migrated = JsonSerializer.Deserialize<TrackingV3>("{}", options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void RegisterMigrator_for_type_without_contract_throws_clear_error()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<NotAMigrator>()));

        Assert.Contains("does not implement any IMigrate<,> contracts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_type_with_explicit_discriminator_property_writes_only_one_discriminator_property()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var payload = new ExistingDiscriminatorType("custom", 42);
        var json = JsonSerializer.Serialize(payload, options);

        Assert.Equal(1, CountOccurrences(json, "\"$type\""));
        Assert.Contains("\"$type\":\"custom\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterMigratorsFromAssembly_throws_when_assembly_is_null()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            options.AddJsonMigrationSupport(builder => builder.RegisterMigratorsFromAssembly(null!)));

        Assert.NotNull(exception.ParamName);
    }

    [Fact]
    public void RegisterMigratorsFromAssemblies_throws_when_assemblies_is_null()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            options.AddJsonMigrationSupport(builder => builder.RegisterMigratorsFromAssemblies(null!)));

        Assert.NotNull(exception.ParamName);
    }

    [Fact]
    public void SetTypeDiscriminatorPropertyName_throws_when_value_is_whitespace()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<ArgumentException>(() =>
            options.AddJsonMigrationSupport(static builder => builder.SetTypeDiscriminatorPropertyName("  ")));

        Assert.Equal("propertyName", exception.ParamName);
    }

    [Fact]
    public void SetMigrationFailureHandling_throws_when_value_is_invalid()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            options.AddJsonMigrationSupport(static builder => builder.SetMigrationFailureHandling((JsonMigrationFailureHandling)999)));

        Assert.Equal("handling", exception.ParamName);
    }

    [Fact]
    public void RegisterMigratorsFromAssemblies_registers_migrator_from_listed_assemblies()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder.RegisterMigratorsFromAssemblies(typeof(ScanMigrator).Assembly));
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);

        var json = JsonSerializer.Serialize(new ScanSource("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<ScanTarget>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
    }

    [Fact]
    public void Deserialize_throws_when_payload_is_empty()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackingV3>(string.Empty, options));

        Assert.Contains("does not contain any JSON tokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_throws_when_external_migrator_returns_false()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<ExternalFalseMigrator>());

        var json = JsonSerializer.Serialize(new ExternalFalseV1("Egil Hansen", 42), options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExternalFalseV2>(json, options));

        Assert.Contains("Migration failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_throws_when_static_migrator_returns_false()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new StaticFalseV1("Egil Hansen", 42), options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StaticFalseV2>(json, options));

        Assert.Contains("Migration failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_falls_back_to_target_when_external_migrator_returns_false_and_policy_is_configured()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder
            .SetMigrationFailureHandling(JsonMigrationFailureHandling.FallBackToTargetType)
            .RegisterMigrator<ExternalFallbackFalseMigrator>());

        var json = JsonSerializer.Serialize(new ExternalFallbackFalseV1("Egil Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<ExternalFallbackFalseV2>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil Hansen", deserialized.Name);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void Deserialize_falls_back_to_target_when_static_migrator_returns_false_and_policy_is_configured()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder
            .SetMigrationFailureHandling(JsonMigrationFailureHandling.FallBackToTargetType));

        var json = JsonSerializer.Serialize(new StaticFallbackFalseV1("Egil Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<StaticFallbackFalseV2>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil Hansen", deserialized.Name);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void Deserialize_uses_attribute_policy_when_migrator_returns_false_and_builder_uses_default_throw()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<AttributeFallbackFalseMigrator>());

        var json = JsonSerializer.Serialize(new AttributeFallbackFalseV1("Egil Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<AttributeFallbackFalseV2>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil Hansen", deserialized.Name);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void Deserialize_returns_null_when_attribute_policy_is_return_null_and_migrator_returns_false()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<AttributeReturnNullFalseMigrator>());

        var json = JsonSerializer.Serialize(new AttributeReturnNullFalseV1("Egil Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<AttributeReturnNullFalseV2>(json, options);

        Assert.Null(deserialized);
    }

    [Fact]
    public void Deserialize_throws_when_return_null_policy_is_used_with_non_nullable_value_type_target()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder
            .SetMigrationFailureHandling(JsonMigrationFailureHandling.ReturnNull)
            .RegisterMigrator<ReturnNullStructFalseMigrator>());

        var json = JsonSerializer.Serialize(new ReturnNullStructV1("Egil Hansen", 42), options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReturnNullStructV2>(json, options));

        Assert.Contains("cannot be applied to non-nullable value type targets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inherited_json_migratable_attribute_failure_policy_overrides_builder_default()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<InheritedPolicyFalseMigrator>());

        var json = JsonSerializer.Serialize(new InheritedPolicySource("Egil Hansen", 42), options);
        var deserialized = JsonSerializer.Deserialize<InheritedPolicyTarget>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil Hansen", deserialized.Name);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void TypeMetadata_uses_type_name_when_type_full_name_is_null()
    {
        Type genericParameterType = typeof(GenericParameterHost<>).GetGenericArguments()[0];
        Type typeMetadataType = typeof(JsonMigrationBuilder).Assembly.GetType(
            "Egil.SystemTextJson.Migration.Migrations.TypeMetadata",
            throwOnError: true)!;

        MethodInfo fromType = typeMetadataType.GetMethod(
            "FromType",
            BindingFlags.Public | BindingFlags.Static)!;

        object metadata = fromType.Invoke(null, [genericParameterType, null, null])!;
        string discriminator = (string)typeMetadataType.GetProperty("Discriminator")!.GetValue(metadata)!;

        Assert.Equal(genericParameterType.Name, discriminator);
    }

    [Fact]
    public void Deserialize_uses_explicit_static_interface_migrator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new ExplicitStaticContractV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<ExplicitStaticContractV2>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.Equal("Hansen", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Deserialize_prefers_interface_contract_method_over_public_shape_match()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new ContractPreferenceV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<ContractPreferenceV2>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("contract", migrated.Path);
        Assert.Equal("Egil", migrated.FirstName);
    }

    public static TheoryData<string, bool, bool, JsonCommentHandling, string> DirectReadFailureCases => new()
    {
        { string.Empty, false, false, JsonCommentHandling.Disallow, "Unexpected end of JSON payload." },
        { "{", true, false, JsonCommentHandling.Disallow, "Unexpected end of JSON payload." },
        { "{/*comment*/\"$type\":\"irrelevant\"}", false, true, JsonCommentHandling.Allow, "Expected 'PropertyName'" },
    };

    [Theory]
    [MemberData(nameof(DirectReadFailureCases))]
    public void DirectRead_throws_for_invalid_reader_states(
        string payload,
        bool advanceReader,
        bool isFinalBlock,
        JsonCommentHandling commentHandling,
        string expectedMessageFragment)
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());
        JsonConverter<TrackingV3> converter = GetTrackingConverter(options);

        var readerState = new JsonReaderState(new JsonReaderOptions { CommentHandling = commentHandling });
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(payload), isFinalBlock, readerState);

        if (advanceReader)
        {
            Assert.True(reader.Read());
        }

        Exception? exception = TryReadTracking(converter, ref reader, options);

        Assert.NotNull(exception);
        Assert.IsType<JsonException>(exception);
        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_treats_payload_as_legacy_when_first_property_is_not_discriminator()
    {
        var options = CreateTrackingOptions(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());

        const string payload = """
            {"firstName":"Egil","lastName":"Hansen","age":42}
            """;

        var migrated = JsonSerializer.Deserialize<TrackingV3>(payload, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Deserialize_uses_source_discriminator_property_name_when_it_differs_from_target()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<CustomDiscriminatorMigrator>());

        var json = JsonSerializer.Serialize(new CustomDiscriminatorSource("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<TrackingV3>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Builder_can_override_default_type_discriminator_property_name()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder
            .SetTypeDiscriminatorPropertyName("__type")
            .RegisterMigrator<TrackingExternalMigrator>());

        var json = JsonSerializer.Serialize(new TrackingV1("Egil Hansen", 42), options);
        Assert.Contains("\"__type\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"$type\":", json, StringComparison.Ordinal);

        var migrated = JsonSerializer.Deserialize<TrackingV3>(json, options);
        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
    }

    [Fact]
    public void Static_migrator_signature_filter_skips_invalid_signatures_and_uses_valid_signature()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new SignatureChaosV1("Egil Hansen", 42), options);
        var migrated = JsonSerializer.Deserialize<SignatureChaosV2>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated!.FirstName);
    }

    [Fact]
    public void Write_falls_back_to_context_type_info_when_typed_target_info_is_unavailable()
    {
        var options = CreateTrackingOptions();
        JsonConverter<TrackingV3> converter = CreateTrackingConverterWithUntypedTargetInfo(options);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        converter.Write(writer, new TrackingV3("Egil", "Hansen", 42), options);
        writer.Flush();

        string json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"firstName\":\"Egil\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_throws_when_context_type_info_deserializes_to_wrong_runtime_type()
    {
        var options = CreateTrackingOptions();
        JsonConverter<TrackingV3> converter = CreateTrackingConverterWithUntypedTargetInfo(options);

        string json = JsonSerializer.Serialize(new TrackingV3("Egil", "Hansen", 42), options);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));

        Exception? exception = TryReadTracking(converter, ref reader, options);

        Assert.IsType<NotSupportedException>(exception);
    }

    private static JsonSerializerOptions CreateTrackingOptions(Action<JsonMigrationBuilder>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(configure);
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);
        return options;
    }

    private static JsonConverter<TrackingV3> GetTrackingConverter(JsonSerializerOptions options)
        => Assert.IsAssignableFrom<JsonConverter<TrackingV3>>(options.GetConverter(typeof(TrackingV3)));

    private static JsonConverter<TrackingV3> CreateTrackingConverterWithUntypedTargetInfo(JsonSerializerOptions options)
    {
        JsonConverter<TrackingV3> converter = GetTrackingConverter(options);
        Type converterType = converter.GetType();
        FieldInfo? contextField = Array.Find(
            converterType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            static field => field.FieldType.Name == "MigratorContext");

        Assert.NotNull(contextField);
        object? originalContext = contextField.GetValue(converter);
        Assert.NotNull(originalContext);

        Type contextType = originalContext.GetType();
        object? targetMetadata = contextType.GetProperty("TargetMetadata")?.GetValue(originalContext);
        object? migratorsByDiscriminator = contextType.GetProperty("MigratorsByDiscriminator")?.GetValue(originalContext);
        object? sourceDiscriminatorPropertyNames = contextType.GetProperty("SourceDiscriminatorPropertyNames")?.GetValue(originalContext);
        object? migrationFailureHandling = contextType.GetProperty("MigrationFailureHandling")?.GetValue(originalContext);

        Assert.NotNull(targetMetadata);
        Assert.NotNull(migratorsByDiscriminator);
        Assert.NotNull(sourceDiscriminatorPropertyNames);
        Assert.NotNull(migrationFailureHandling);

        JsonTypeInfo untypedTargetTypeInfo = options.GetTypeInfo(typeof(object));
        object mismatchedContext = Activator.CreateInstance(
            contextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [untypedTargetTypeInfo, targetMetadata, migratorsByDiscriminator, sourceDiscriminatorPropertyNames, migrationFailureHandling],
            culture: null)!;

        object converterInstance = Activator.CreateInstance(
            converterType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [mismatchedContext],
            culture: null)!;

        return Assert.IsAssignableFrom<JsonConverter<TrackingV3>>(converterInstance);
    }

    private static Exception? TryReadTracking(JsonConverter<TrackingV3> converter, ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        try
        {
            _ = converter.Read(ref reader, typeof(TrackingV3), options);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private sealed class NotAMigrator;

    [JsonMigratable]
    public record class ExternalFalseV1(string Name, int Age);

    [JsonMigratable]
    public record class ExternalFalseV2(string FirstName, string LastName, int Age);

    public sealed class ExternalFalseMigrator : IMigrate<ExternalFalseV1, ExternalFalseV2>
    {
        public bool TryMigrateFrom(ExternalFalseV1 source, out ExternalFalseV2 result)
        {
            result = new ExternalFalseV2(string.Empty, string.Empty, source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class StaticFalseV1(string Name, int Age);

    [JsonMigratable]
    public record class StaticFalseV2(string FirstName, string LastName, int Age) :
        IMigrateFrom<StaticFalseV1, StaticFalseV2>
    {
        public static bool TryMigrateFrom(StaticFalseV1 source, out StaticFalseV2 result)
        {
            result = new StaticFalseV2(string.Empty, string.Empty, source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class ExistingDiscriminatorType(
        [property: JsonPropertyName("$type")] string ExplicitDiscriminator,
        int Age);

    [JsonMigratable]
    public record class ExplicitStaticContractV1(string Name, int Age);

    [JsonMigratable]
    public record class ExplicitStaticContractV2(string FirstName, string LastName, int Age) :
        IMigrateFrom<ExplicitStaticContractV1, ExplicitStaticContractV2>
    {
        static bool IMigrateFrom<ExplicitStaticContractV1, ExplicitStaticContractV2>.TryMigrateFrom(
            ExplicitStaticContractV1 source,
            out ExplicitStaticContractV2 result)
        {
            string[] names = source.Name.Split(' ', 2, StringSplitOptions.TrimEntries);
            string firstName = names[0];
            string lastName = names.Length == 2 ? names[1] : string.Empty;
            result = new ExplicitStaticContractV2(firstName, lastName, source.Age);
            return true;
        }
    }

    [JsonMigratable]
    public record class ContractPreferenceV1(string Name, int Age);

    [JsonMigratable]
    public record class ContractPreferenceV2(string FirstName, string LastName, int Age, string Path) :
        IMigrateFrom<ContractPreferenceV1, ContractPreferenceV2>
    {
        public static bool TryMigrateFrom(ContractPreferenceV1 source, out ContractPreferenceV2 result)
        {
            result = new ContractPreferenceV2("public", string.Empty, source.Age, "shape");
            return true;
        }

        static bool IMigrateFrom<ContractPreferenceV1, ContractPreferenceV2>.TryMigrateFrom(
            ContractPreferenceV1 source,
            out ContractPreferenceV2 result)
        {
            string[] names = source.Name.Split(' ', 2, StringSplitOptions.TrimEntries);
            string firstName = names[0];
            string lastName = names.Length == 2 ? names[1] : string.Empty;
            result = new ContractPreferenceV2(firstName, lastName, source.Age, "contract");
            return true;
        }
    }

    [JsonMigratable]
    public record class SignatureChaosV1(string Name, int Age);

    [JsonMigratable]
    public record class SignatureChaosV2(string FirstName, string LastName, int Age) :
        IMigrateFrom<SignatureChaosV1, SignatureChaosV2>
    {
        public static bool TryMigrateFrom() => false;

        public static bool TryMigrateFrom(SignatureChaosV1 source) => false;

        public static bool TryMigrateFrom(SignatureChaosV1 source, SignatureChaosV2 result) => false;

        public static bool TryMigrateFrom(int source, out SignatureChaosV2 result)
        {
            result = new SignatureChaosV2(string.Empty, string.Empty, source);
            return false;
        }

        public static bool TryMigrateFrom(SignatureChaosV1 source, out SignatureChaosV2 result, int version)
        {
            result = new SignatureChaosV2(source.Name, string.Empty, source.Age);
            return false;
        }

        public static bool TryMigrateFrom(SignatureChaosV1 source, [Out] object result) => false;

        public static bool TryMigrateFrom(SignatureChaosV1 source, out SignatureChaosV2 result)
        {
            string[] names = source.Name.Split(' ', 2, StringSplitOptions.TrimEntries);
            string firstName = names[0];
            string lastName = names.Length == 2 ? names[1] : string.Empty;
            result = new SignatureChaosV2(firstName, lastName, source.Age);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminatorPropertyName = "kind")]
    public record class CustomDiscriminatorSource(string Name, int Age);

    public sealed class CustomDiscriminatorMigrator : IMigrate<CustomDiscriminatorSource, TrackingV3>
    {
        public bool TryMigrateFrom(CustomDiscriminatorSource source, out TrackingV3 result)
        {
            string[] names = source.Name.Split(' ', 2, StringSplitOptions.TrimEntries);
            string firstName = names[0];
            string lastName = names.Length == 2 ? names[1] : string.Empty;
            result = new TrackingV3(firstName, lastName, source.Age);
            return true;
        }
    }

    [JsonMigratable]
    public record class ExternalFallbackFalseV1(string Name, int Age);

    [JsonMigratable]
    public record class ExternalFallbackFalseV2(string Name, int Age);

    public sealed class ExternalFallbackFalseMigrator : IMigrate<ExternalFallbackFalseV1, ExternalFallbackFalseV2>
    {
        public bool TryMigrateFrom(ExternalFallbackFalseV1 source, out ExternalFallbackFalseV2 result)
        {
            result = new ExternalFallbackFalseV2("migrator-result-ignored", source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class StaticFallbackFalseV1(string Name, int Age);

    [JsonMigratable]
    public record class StaticFallbackFalseV2(string Name, int Age) :
        IMigrateFrom<StaticFallbackFalseV1, StaticFallbackFalseV2>
    {
        public static bool TryMigrateFrom(StaticFallbackFalseV1 source, out StaticFallbackFalseV2 result)
        {
            result = new StaticFallbackFalseV2("migrator-result-ignored", source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class AttributeFallbackFalseV1(string Name, int Age);

    [JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.FallBackToTargetType)]
    public record class AttributeFallbackFalseV2(string Name, int Age);

    public sealed class AttributeFallbackFalseMigrator : IMigrate<AttributeFallbackFalseV1, AttributeFallbackFalseV2>
    {
        public bool TryMigrateFrom(AttributeFallbackFalseV1 source, out AttributeFallbackFalseV2 result)
        {
            result = new AttributeFallbackFalseV2("migrator-result-ignored", source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class AttributeReturnNullFalseV1(string Name, int Age);

    [JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
    public record class AttributeReturnNullFalseV2(string Name, int Age);

    public sealed class AttributeReturnNullFalseMigrator : IMigrate<AttributeReturnNullFalseV1, AttributeReturnNullFalseV2>
    {
        public bool TryMigrateFrom(AttributeReturnNullFalseV1 source, out AttributeReturnNullFalseV2 result)
        {
            result = new AttributeReturnNullFalseV2("migrator-result-ignored", source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class ReturnNullStructV1(string Name, int Age);

    [JsonMigratable]
    public readonly record struct ReturnNullStructV2(string Name, int Age);

    public sealed class ReturnNullStructFalseMigrator : IMigrate<ReturnNullStructV1, ReturnNullStructV2>
    {
        public bool TryMigrateFrom(ReturnNullStructV1 source, out ReturnNullStructV2 result)
        {
            result = new ReturnNullStructV2("ignored", source.Age);
            return false;
        }
    }

    [JsonMigratable]
    public record class InheritedPolicySource(string Name, int Age);

    [JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.FallBackToTargetType)]
    public record class InheritedPolicyBase(string Name, int Age);

    public record class InheritedPolicyTarget(string Name, int Age) : InheritedPolicyBase(Name, Age);

    public sealed class InheritedPolicyFalseMigrator : IMigrate<InheritedPolicySource, InheritedPolicyTarget>
    {
        public bool TryMigrateFrom(InheritedPolicySource source, out InheritedPolicyTarget result)
        {
            result = new InheritedPolicyTarget("ignored", source.Age);
            return false;
        }
    }

    private sealed class GenericParameterHost<T>;
}
