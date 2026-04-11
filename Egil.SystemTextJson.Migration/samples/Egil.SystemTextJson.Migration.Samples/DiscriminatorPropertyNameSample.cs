namespace Egil.SystemTextJson.Migration.Samples.DiscriminatorConfig;

[JsonMigratable(TypeDiscriminator = "setting-v1")]
public record class SettingV1(string Key, string Value);

[JsonMigratable(TypeDiscriminator = "setting-v2")]
public record class SettingV2(string Key, string Value, string? Category)
    : IMigrateFrom<SettingV1, SettingV2>
{
    public static bool TryMigrateFrom(SettingV1 source, out SettingV2 result)
    {
        result = new SettingV2(source.Key, source.Value, "general");
        return true;
    }
}

public sealed class DiscriminatorPropertyNameSampleTests
{
    [Fact]
    public void Custom_discriminator_property_name()
    {
        #region discriminator_property_name
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder.SetTypeDiscriminatorPropertyName("_schema"));

        // Serialization uses the custom property name
        var setting = new SettingV2("theme", "dark", "ui");
        var json = JsonSerializer.Serialize(setting, options);
        // json contains "_schema":"setting-v2" instead of "$type":"setting-v2"

        // Deserialization reads the custom property name
        var legacyJson = """{"_schema":"setting-v1","key":"theme","value":"dark"}""";
        var migrated = JsonSerializer.Deserialize<SettingV2>(legacyJson, options);
        #endregion

        Assert.Contains("\"_schema\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"$type\":", json, StringComparison.Ordinal);
        Assert.NotNull(migrated);
        Assert.Equal("general", migrated.Category);
    }
}
