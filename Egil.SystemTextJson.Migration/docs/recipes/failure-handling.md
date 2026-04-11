# Failure Handling

## Default failure behavior

When `TryMigrateFrom` returns `false`, the library throws a `JsonException` by default. This is the safest behavior — it prevents silently corrupted data.

```csharp
// With the default ThrowJsonException policy:
// If TryMigrateFrom returns false → JsonException is thrown.
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();
```

> **Note:** This default ensures that migration failures are never swallowed silently. Handle the `JsonException` at the call site if you need graceful degradation.

## Falling back to target type deserialization

Set `FallBackToTargetType` to attempt deserializing the original JSON directly as the target type when migration fails:

<!-- snippet: failure_handling_builder -->
<a id='snippet-failure_handling_builder'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.SetMigrationFailureHandling(
        JsonMigrationFailureHandling.FallBackToTargetType);
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/FailureHandlingSample.cs#L41-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-failure_handling_builder' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This is useful when old and new schemas are partially compatible — the target type can deserialize from the source JSON with some properties missing or using default values.

## Returning null on failure

Set `ReturnNull` to silently return `null` when migration fails:

<!-- snippet: failure_handling_return_null -->
<a id='snippet-failure_handling_return_null'></a>
```cs
// Per-type override:
[JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record OptionalData(string Value);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/FailureHandlingSample.cs#L17-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-failure_handling_return_null' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Only use this when the target type is nullable in your domain model. This is useful for optional data where a missing or unmigrateable value is acceptable.

## Per-type failure handling override

Override the global failure policy on individual types using the `[JsonMigratable]` attribute:

<!-- snippet: per_type_failure_handling -->
<a id='snippet-per_type_failure_handling'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/PerTypeFailureSample.cs#L6-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-per_type_failure_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: per_type_failure_usage -->
<a id='snippet-per_type_failure_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// Global policy is ThrowJsonException (default), but OptionalDataV2
// overrides this to ReturnNull on its [JsonMigratable] attribute.
options.AddJsonMigrationSupport();

var json = """{"$type":"optional-v1","rawData":"invalid-format"}""";
var result = JsonSerializer.Deserialize<OptionalDataV2>(json, options);
// result is null — migration failed gracefully
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/PerTypeFailureSample.cs#L55-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-per_type_failure_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use `FallBackToTargetType` on a per-type basis:

<!-- snippet: per_type_fallback -->
<a id='snippet-per_type_fallback'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/PerTypeFailureSample.cs#L29-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-per_type_fallback' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: per_type_fallback_usage -->
<a id='snippet-per_type_fallback_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Migration fails, but FallBackToTargetType means the library
// tries deserializing the JSON directly as AppConfigV2.
// Since the old JSON has "data" but not "extra", Extra will be null.
var json = """{"$type":"config-v1","data":"production"}""";
var config = JsonSerializer.Deserialize<AppConfigV2>(json, options);
// config.Data == "production", config.Extra == null
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/PerTypeFailureSample.cs#L85-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-per_type_fallback_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The per-type policy on `[JsonMigratable]` always overrides the global policy set via `SetMigrationFailureHandling()` on the builder.
