using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public sealed class DiscriminatorPropertyKeyTests
{
    [Theory]
    [InlineData("$type")]
    [InlineData("schema")]
    public void Same_value_under_distinct_properties_selects_the_correct_source(string defaultProperty)
    {
        var options = CreateOptions(defaultProperty);
        var defaultJson = JsonSerializer.Serialize(new DefaultSource("one"), options);
        var overrideJson = JsonSerializer.Serialize(new OverrideSource("two"), options);
        var caseJson = JsonSerializer.Serialize(new CaseSource("three"), options);

        var fromDefault = JsonSerializer.Deserialize<Current>(defaultJson, options);
        var fromOverride = JsonSerializer.Deserialize<Current>(overrideJson, options);
        var fromCase = JsonSerializer.Deserialize<Current>(caseJson, options);

        Assert.Equal(new Current("one", "static-default"), fromDefault);
        Assert.Equal(new Current("two", "external-override"), fromOverride);
        Assert.Equal(new Current("three", "external-case"), fromCase);
    }

    [Fact]
    public void Builder_default_matching_an_explicit_source_property_still_rejects_duplicate_pair()
    {
        var options = CreateOptions("legacyKind");

        var exception = Assert.Throws<JsonMigrationDuplicateTypeDiscriminatorException>(
            () => JsonSerializer.Deserialize<Current>("{}", options));

        Assert.Equal(typeof(Current), exception.TargetType);
        Assert.Equal("shared", exception.Discriminator);
    }

    [Theory]
    [InlineData("{\"$type\":\"unknown\",\"value\":\"one\"}")]
    [InlineData("{\"legacyKind\":\"unknown\",\"value\":\"one\"}")]
    public void Unknown_value_under_a_known_property_throws(string json)
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Current>(json, CreateOptions("$type")));
        Assert.Contains("No migrator was found", exception.Message);
    }

    [Theory]
    [InlineData("$type", "legacyKind")]
    [InlineData("legacyKind", "$type")]
    public void Multiple_matching_source_properties_throw(string first, string second)
    {
        var json = $$"""{"{{first}}":"shared","value":"one","{{second}}":"shared"}""";
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Current>(json, CreateOptions("$type")));
        Assert.Contains("Multiple discriminator properties", exception.Message);
    }

    [Theory]
    [InlineData("{\"$type\":\"current\",\"legacyKind\":\"shared\",\"value\":\"one\",\"path\":\"current\"}")]
    [InlineData("{\"legacyKind\":\"shared\",\"$type\":\"current\",\"value\":\"one\",\"path\":\"current\"}")]
    public void Matching_target_and_source_properties_throw_in_either_order(string json)
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Current>(json, CreateOptions("$type")));
        Assert.Contains("Multiple discriminator properties", exception.Message);
    }

    [Theory]
    [InlineData("{\"$type\":\"current\",\"legacyKind\":\"unique\",\"value\":\"one\",\"path\":\"current\"}")]
    [InlineData("{\"legacyKind\":\"unique\",\"$type\":\"current\",\"value\":\"one\",\"path\":\"current\"}")]
    public void Target_and_unique_source_values_under_distinct_properties_are_ambiguous(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder
            .GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute =>
                attribute.TypeDiscriminatorPropertyName == "legacyKind" ? "unique" : attribute.TypeDiscriminator)
            .RegisterMigrator<OverrideMigrator>());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Current>(json, options));

        Assert.Contains("Multiple discriminator properties", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unambiguous_target_payload_still_deserializes(bool sharedValues)
    {
        var options = sharedValues ? CreateOptions("$type") : new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (!sharedValues)
        {
            options.AddJsonMigrationSupport();
        }

        var current = new Current("one", "current");
        var json = JsonSerializer.Serialize(current, options);

        Assert.Equal(current, JsonSerializer.Deserialize<Current>(json, options));
    }

    [Fact]
    public void Nested_matching_property_does_not_make_the_root_ambiguous()
    {
        var json = """{"$type":"shared","value":"one","extra":{"legacyKind":"shared"}}""";
        Assert.Equal(new Current("one", "static-default"), JsonSerializer.Deserialize<Current>(json, CreateOptions("$type")));
    }

    [Theory]
    [InlineData("$type", "override-only")]
    [InlineData("legacyKind", "shared")]
    public void A_value_registered_under_another_property_does_not_match(string property, string discriminator)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder
            .GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute =>
                attribute.TypeDiscriminatorPropertyName == "legacyKind" ? "override-only" : attribute.TypeDiscriminator)
            .RegisterMigrator<OverrideMigrator>());
        var json = $$"""{"{{property}}":"{{discriminator}}","value":"one"}""";

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Current>(json, options));

        Assert.Contains("No migrator was found", exception.Message);
    }

    private static JsonSerializerOptions CreateOptions(string defaultProperty)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder
            .SetTypeDiscriminatorPropertyName(defaultProperty)
            .RegisterMigrator<DefaultExternalMigrator>()
            .RegisterMigrator<OverrideMigrator>()
            .RegisterMigrator<CaseMigrator>());
        return options;
    }

    [JsonMigratable(TypeDiscriminator = "shared")]
    public sealed record DefaultSource(string Value);

    [JsonMigratable(TypeDiscriminator = "shared", TypeDiscriminatorPropertyName = "legacyKind")]
    public sealed record OverrideSource(string Value);

    [JsonMigratable(TypeDiscriminator = "shared", TypeDiscriminatorPropertyName = "LegacyKind")]
    public sealed record CaseSource(string Value);

    [JsonMigratable(TypeDiscriminator = "current")]
    public sealed record Current(string Value, string Path) : IMigrateFrom<DefaultSource, Current>
    {
        public static bool TryMigrateFrom(DefaultSource source, out Current result)
        {
            result = new Current(source.Value, "static-default");
            return true;
        }
    }

    public sealed class DefaultExternalMigrator : IMigrate<DefaultSource, Current>
    {
        public bool TryMigrateFrom(DefaultSource source, out Current result)
        {
            result = new Current(source.Value, "external-default");
            return true;
        }
    }

    public sealed class OverrideMigrator : IMigrate<OverrideSource, Current>
    {
        public bool TryMigrateFrom(OverrideSource source, out Current result)
        {
            result = new Current(source.Value, "external-override");
            return true;
        }
    }

    public sealed class CaseMigrator : IMigrate<CaseSource, Current>
    {
        public bool TryMigrateFrom(CaseSource source, out Current result)
        {
            result = new Current(source.Value, "external-case");
            return true;
        }
    }
}
