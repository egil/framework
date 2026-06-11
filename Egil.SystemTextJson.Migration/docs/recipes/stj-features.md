# Non-Library STJ Features Related to Migration

These are built-in `System.Text.Json` interfaces that complement the migration library. They are not part of this library, but they are commonly needed in migration scenarios.

> **Note on `[JsonPolymorphic]` / `[JsonDerivedType]`:** These STJ attributes **cannot** be combined with `[JsonMigratable]` on the same type hierarchy. See [polymorphism.md](polymorphism.md) for the reason and recommended workarounds.

## Using `IJsonOnDeserialized` for post-migration validation

Implement `IJsonOnDeserialized` on the target type to validate or normalize data after deserialization. This runs for current-format payloads deserialized by STJ:

<!-- snippet: stj_on_deserialized_type -->
<a id='snippet-stj_on_deserialized_type'></a>
```cs
[JsonMigratable(TypeDiscriminator = "profile-v2")]
public class ProfileV2 : IJsonOnDeserialized
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    public void OnDeserialized()
    {
        // Compute DisplayName if not already set — works for both
        // fresh deserialization and post-migration payloads.
        DisplayName ??= $"{FirstName} {LastName}";
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L3-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_on_deserialized_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: stj_on_deserialized_usage -->
<a id='snippet-stj_on_deserialized_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

var json = """{"$type":"profile-v2","firstName":"Jane","lastName":"Doe"}""";
var profile = JsonSerializer.Deserialize<ProfileV2>(json, options);
// profile.DisplayName is "Jane Doe" — set by OnDeserialized()
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L86-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_on_deserialized_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** `OnDeserialized()` is called by System.Text.Json after populating the object's properties. For migrated objects (created by a migrator, not by STJ), this callback is **not** invoked — the migrator is responsible for setting up the object correctly. Use `IJsonMigrationTracked` to detect migration at the application level.

## Using `IJsonOnSerializing` to prepare data before serialization

Implement `IJsonOnSerializing` to set computed or derived fields just before the type is written to JSON:

<!-- snippet: stj_on_serializing_type -->
<a id='snippet-stj_on_serializing_type'></a>
```cs
[JsonMigratable(TypeDiscriminator = "event-v2")]
public class EventV2 : IJsonOnSerializing
{
    public string Title { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }

    [JsonPropertyName("startFormatted")]
    public string? StartFormatted { get; set; }

    public void OnSerializing()
    {
        // Maintain a backward-compatible formatted date string
        // so older consumers can still read the payload.
        StartFormatted = StartUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L20-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_on_serializing_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: stj_on_serializing_usage -->
<a id='snippet-stj_on_serializing_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

var evt = new EventV2 { Title = "Launch", StartUtc = new DateTime(2025, 6, 15, 14, 30, 0) };
var json = JsonSerializer.Serialize(evt, options);
// JSON includes "startFormatted":"2025-06-15 14:30" for older consumers
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L102-L109' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_on_serializing_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This is useful for maintaining backward-compatible fields. When the schema evolves, `OnSerializing()` can populate legacy fields that older consumers still expect.

## Combining `IJsonOnDeserialized` with `IJsonMigrationTracked`

For current-format payloads, `OnDeserialized()` handles computed fields. For migrated payloads, the migrator handles setup, and the application checks `MigratedDuringDeserialization` for any post-migration actions:

<!-- snippet: stj_combined_tracked_type -->
<a id='snippet-stj_combined_tracked_type'></a>
```cs
[JsonMigratable(TypeDiscriminator = "config-v1")]
public record class ConfigV1(string ConnectionString);

[JsonMigratable(TypeDiscriminator = "config-v2")]
public class ConfigV2 : IJsonMigrationTracked, IJsonOnDeserialized,
    IMigrateFrom<ConfigV1, ConfigV2>
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }

    // Computed during OnDeserialized for current-format payloads.
    [JsonIgnore]
    public string ConnectionString { get; set; } = string.Empty;

    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public void OnDeserialized()
    {
        // Runs for non-migrated (current-format) payloads.
        // Compute derived fields that aren't stored in JSON.
        ConnectionString = $"{Host}:{Port}";
    }

    public static bool TryMigrateFrom(ConfigV1 source, out ConfigV2 result)
    {
        // Parse "host:port" from old connection string format
        var parts = source.ConnectionString.Split(':');
        result = new ConfigV2
        {
            Host = parts.Length > 0 ? parts[0] : "localhost",
            Port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5432,
        };
        // Set computed fields that OnDeserialized would normally handle,
        // since OnDeserialized is not called for migrated objects.
        result.ConnectionString = $"{result.Host}:{result.Port}";
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L39-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_combined_tracked_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: stj_combined_tracked_usage -->
<a id='snippet-stj_combined_tracked_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Old config format — will be migrated
var legacyJson = """{"$type":"config-v1","connectionString":"db.example.com:5432"}""";
var config = JsonSerializer.Deserialize<ConfigV2>(legacyJson, options);

// After deserialization, check if the object was migrated.
// Use this to trigger write-back, logging, or other side effects.
if (config is { MigratedDuringDeserialization: true })
{
    // Flag for resave, emit a log entry, etc.
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StjCallbacksSample.cs#L118-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-stj_combined_tracked_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The migrator (`TryMigrateFrom`) should set up the object completely, including any computed fields that `OnDeserialized()` would normally handle. After deserialization, check `MigratedDuringDeserialization` at the application level for any migration-specific side effects (logging, write-back, etc.).
