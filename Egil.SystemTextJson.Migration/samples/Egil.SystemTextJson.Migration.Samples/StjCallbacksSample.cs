namespace Egil.SystemTextJson.Migration.Samples.StjCallbacks;

#region stj_on_deserialized_type
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
#endregion

#region stj_on_serializing_type
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
#endregion

#region stj_combined_tracked_type
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
#endregion

public sealed class StjCallbacksSampleTests
{
    [Fact]
    public void OnDeserialized_sets_computed_field()
    {
        #region stj_on_deserialized_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"$type":"profile-v2","firstName":"Egil","lastName":"Hansen"}""";
        var profile = JsonSerializer.Deserialize<ProfileV2>(json, options);
        // profile.DisplayName is "Egil Hansen" — set by OnDeserialized()
        #endregion

        Assert.NotNull(profile);
        Assert.Equal("Egil Hansen", profile.DisplayName);
    }

    [Fact]
    public void OnSerializing_writes_backward_compat_field()
    {
        #region stj_on_serializing_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var evt = new EventV2 { Title = "Launch", StartUtc = new DateTime(2025, 6, 15, 14, 30, 0) };
        var json = JsonSerializer.Serialize(evt, options);
        // JSON includes "startFormatted":"2025-06-15 14:30" for older consumers
        #endregion

        Assert.Contains("startFormatted", json, StringComparison.Ordinal);
        Assert.Contains("2025-06-15 14:30", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Combined_tracking_with_on_deserialized()
    {
        #region stj_combined_tracked_usage
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
        #endregion

        Assert.NotNull(config);
        Assert.True(config.MigratedDuringDeserialization);
        Assert.Equal("db.example.com", config.Host);
        Assert.Equal(5432, config.Port);
        Assert.Equal("db.example.com:5432", config.ConnectionString);
    }

    [Fact]
    public void Current_payload_calls_on_deserialized()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Current format — OnDeserialized() computes ConnectionString
        var currentJson = """{"$type":"config-v2","host":"db.example.com","port":5432}""";
        var config = JsonSerializer.Deserialize<ConfigV2>(currentJson, options);

        Assert.NotNull(config);
        Assert.False(config.MigratedDuringDeserialization);
        Assert.Equal("db.example.com:5432", config.ConnectionString);
    }
}
