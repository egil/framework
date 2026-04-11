namespace Egil.SystemTextJson.Migration.Samples.PerTypeFailure;

[JsonMigratable(TypeDiscriminator = "optional-v1")]
public record class OptionalDataV1(string RawData);

#region per_type_failure_handling
// Per-type override: return null when migration fails,
// even if the global policy is ThrowJsonException.
[JsonMigratable(
    TypeDiscriminator = "optional-v2",
    MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record class OptionalDataV2(string ParsedValue)
    : IMigrateFrom<OptionalDataV1, OptionalDataV2>
{
    public static bool TryMigrateFrom(OptionalDataV1 source, out OptionalDataV2 result)
    {
        if (source.RawData.StartsWith("valid:", StringComparison.Ordinal))
        {
            result = new OptionalDataV2(source.RawData[6..]);
            return true;
        }

        result = default!;
        return false; // Migration fails → returns null (per-type policy)
    }
}
#endregion

#region per_type_fallback
// Per-type override: fall back to target-type deserialization on failure.
[JsonMigratable(
    TypeDiscriminator = "config-v1",
    MigrationFailureHandling = JsonMigrationFailureHandling.FallBackToTargetType)]
public record class AppConfigV1(string Data);

[JsonMigratable(
    TypeDiscriminator = "config-v2",
    MigrationFailureHandling = JsonMigrationFailureHandling.FallBackToTargetType)]
public record class AppConfigV2(string Data, string? Extra)
    : IMigrateFrom<AppConfigV1, AppConfigV2>
{
    public static bool TryMigrateFrom(AppConfigV1 source, out AppConfigV2 result)
    {
        result = default!;
        return false; // Fail → system falls back to deserializing as AppConfigV2
    }
}
#endregion

public sealed class PerTypeFailureSampleTests
{
    [Fact]
    public void Return_null_on_migration_failure()
    {
        #region per_type_failure_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Global policy is ThrowJsonException (default), but OptionalDataV2
        // overrides this to ReturnNull on its [JsonMigratable] attribute.
        options.AddJsonMigrationSupport();

        var json = """{"$type":"optional-v1","rawData":"invalid-format"}""";
        var result = JsonSerializer.Deserialize<OptionalDataV2>(json, options);
        // result is null — migration failed gracefully
        #endregion

        Assert.Null(result);
    }

    [Fact]
    public void Successful_migration_still_works()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"$type":"optional-v1","rawData":"valid:hello world"}""";
        var result = JsonSerializer.Deserialize<OptionalDataV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("hello world", result.ParsedValue);
    }

    [Fact]
    public void Fallback_to_target_type_on_failure()
    {
        #region per_type_fallback_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Migration fails, but FallBackToTargetType means the library
        // tries deserializing the JSON directly as AppConfigV2.
        // Since the old JSON has "data" but not "extra", Extra will be null.
        var json = """{"$type":"config-v1","data":"production"}""";
        var config = JsonSerializer.Deserialize<AppConfigV2>(json, options);
        // config.Data == "production", config.Extra == null
        #endregion

        Assert.NotNull(config);
        Assert.Equal("production", config.Data);
        Assert.Null(config.Extra);
    }
}
