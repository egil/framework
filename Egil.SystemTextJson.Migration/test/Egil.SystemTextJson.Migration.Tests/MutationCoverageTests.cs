using System.Text.Json;
using System.Text.Json.Serialization;

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

    private static JsonSerializerOptions CreateTrackingOptions(Action<JsonMigrationBuilder>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(configure);
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);
        return options;
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
    public record class ExistingDiscriminatorType(
        [property: JsonPropertyName("$type")] string ExplicitDiscriminator,
        int Age);
}
