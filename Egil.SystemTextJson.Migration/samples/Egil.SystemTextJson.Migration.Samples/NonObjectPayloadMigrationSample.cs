namespace Egil.SystemTextJson.Migration.Samples.NonObjectPayloadMigration;

#region non_object_list_types
// The target type accepts a List<string> as its source.
// The source type (List<string>) is NOT marked with [JsonMigratable]
// — it's a plain .NET collection whose JSON representation is an array.
[JsonMigratable(TypeDiscriminator = "settings-v2")]
public record SettingsV2(List<string> Tags, string Label)
    : IMigrateFrom<List<string>, SettingsV2>
{
    public static bool TryMigrateFrom(List<string> source, out SettingsV2 result)
    {
        result = new SettingsV2(source, "migrated");
        return true;
    }
}
#endregion

#region non_object_string_type
// Migrate from a raw JSON string to a structured type.
[JsonMigratable(TypeDiscriminator = "label-v2")]
public record LabelV2(string Value, string NormalizedValue)
    : IMigrateFrom<string, LabelV2>
{
    public static bool TryMigrateFrom(string source, out LabelV2 result)
    {
        result = new LabelV2(source, source.ToLowerInvariant().Trim());
        return true;
    }
}
#endregion

#region non_object_mixed_migrators
// A target that can migrate from both an older object-based
// version AND from a plain collection.
[JsonMigratable(TypeDiscriminator = "config-v1")]
public record ConfigV1(string CsvItems);

[JsonMigratable(TypeDiscriminator = "config-v2")]
public record ConfigV2(List<string> Items, string Source)
    : IMigrateFrom<List<string>, ConfigV2>,
      IMigrateFrom<ConfigV1, ConfigV2>
{
    public static bool TryMigrateFrom(List<string> source, out ConfigV2 result)
    {
        result = new ConfigV2(source, "from-list");
        return true;
    }

    public static bool TryMigrateFrom(ConfigV1 source, out ConfigV2 result)
    {
        result = new ConfigV2(
            [.. source.CsvItems.Split(',', StringSplitOptions.RemoveEmptyEntries)],
            "from-config-v1");
        return true;
    }
}
#endregion

#region non_object_dict_type
// Migrate from a Dictionary<string, string> to a structured type.
// Dictionaries serialize as JSON objects, but the library detects
// there is no discriminator and matches by JsonTypeInfoKind.Dictionary.
[JsonMigratable(TypeDiscriminator = "port-config-v2")]
public record PortConfigV2(Dictionary<string, string> Ports, int Version)
    : IMigrateFrom<Dictionary<string, string>, PortConfigV2>
{
    public static bool TryMigrateFrom(Dictionary<string, string> source, out PortConfigV2 result)
    {
        result = new PortConfigV2(source, 2);
        return true;
    }
}
#endregion

public class NonObjectPayloadMigrationTests
{
    [Fact]
    public void Migrate_list_of_strings_to_structured_type()
    {
        #region non_object_list_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Stored JSON is a plain array — no $type, no object wrapper.
        var json = """["csharp","dotnet","azure"]""";
        SettingsV2 settings = JsonSerializer.Deserialize<SettingsV2>(json, options)!;
        // settings.Tags = ["csharp", "dotnet", "azure"], settings.Label = "migrated"
        #endregion

        Assert.Equal(["csharp", "dotnet", "azure"], settings.Tags);
        Assert.Equal("migrated", settings.Label);
    }

    [Fact]
    public void Migrate_string_to_structured_type()
    {
        #region non_object_string_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Stored JSON is a raw string value.
        var json = """ "  Hello World  " """;
        LabelV2 label = JsonSerializer.Deserialize<LabelV2>(json, options)!;
        // label.Value = "  Hello World  ", label.NormalizedValue = "hello world"
        #endregion

        Assert.Equal("  Hello World  ", label.Value);
        Assert.Equal("hello world", label.NormalizedValue);
    }

    [Fact]
    public void Mixed_migrators_array_payload_uses_list_migrator()
    {
        #region non_object_mixed_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Array payload → matches the List<string> migrator
        var arrayJson = """["alpha","beta"]""";
        ConfigV2 fromList = JsonSerializer.Deserialize<ConfigV2>(arrayJson, options)!;
        // fromList.Items = ["alpha", "beta"], fromList.Source = "from-list"

        // Object payload with discriminator → matches the ConfigV1 migrator
        var objectJson = """{"$type":"config-v1","csvItems":"alpha,beta"}""";
        ConfigV2 fromV1 = JsonSerializer.Deserialize<ConfigV2>(objectJson, options)!;
        // fromV1.Items = ["alpha", "beta"], fromV1.Source = "from-config-v1"
        #endregion

        Assert.Equal(["alpha", "beta"], fromList.Items);
        Assert.Equal("from-list", fromList.Source);

        Assert.Equal(["alpha", "beta"], fromV1.Items);
        Assert.Equal("from-config-v1", fromV1.Source);
    }

    [Fact]
    public void Round_trip_after_migration_uses_happy_path()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // First: migrate from array
        var settings = JsonSerializer.Deserialize<SettingsV2>("""["a","b"]""", options)!;

        // Serialize — now includes $type discriminator
        var json = JsonSerializer.Serialize(settings, options);
        Assert.StartsWith("{\"$type\":", json, StringComparison.Ordinal);

        // Deserialize again — takes the happy path (no migration)
        var roundTripped = JsonSerializer.Deserialize<SettingsV2>(json, options)!;
        Assert.Equal(settings.Tags, roundTripped.Tags);
    }

    [Fact]
    public void Migrate_dictionary_to_structured_type()
    {
        #region non_object_dict_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Stored JSON is a dictionary — a JSON object with no $type discriminator.
        var json = """{"redis":"6379","postgres":"5432"}""";
        PortConfigV2 config = JsonSerializer.Deserialize<PortConfigV2>(json, options)!;
        // config.Ports = {"redis": "6379", "postgres": "5432"}, config.Version = 2
        #endregion

        Assert.Equal(2, config.Ports.Count);
        Assert.Equal("6379", config.Ports["redis"]);
        Assert.Equal("5432", config.Ports["postgres"]);
        Assert.Equal(2, config.Version);
    }
}
