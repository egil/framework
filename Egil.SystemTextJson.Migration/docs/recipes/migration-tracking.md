# Migration Tracking

## Detecting whether a value was migrated

Implement `IJsonMigrationTracked` on the target type. After deserialization, check `MigratedDuringDeserialization` to know whether migration occurred:

<!-- snippet: migration_tracking_type -->
<a id='snippet-migration_tracking_type'></a>
```cs
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IJsonMigrationTracked, IMigrateFrom<UserV1, UserV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/MigrationTrackingSample.cs#L6-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-migration_tracking_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: migration_tracking_usage -->
<a id='snippet-migration_tracking_usage'></a>
```cs
// After deserialization:
var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
if (user.MigratedDuringDeserialization)
{
    // Persist the updated representation so future reads
    // hit the happy path.
    // await SaveAsync(user);
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/MigrationTrackingSample.cs#L31-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-migration_tracking_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The property is set to `true` when the object was created by a migrator and `false` when the JSON matched the target type directly. Mark the property with `[JsonIgnore]` so it is not persisted.

## Read-migrate-write-back pattern

Combine `IJsonMigrationTracked` with persistence to eliminate future migration overhead. After deserializing, check if migration occurred and re-persist the data in the current format:

<!-- snippet: read_migrate_write_back_types -->
<a id='snippet-read_migrate_write_back_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "document-v2")]
public record class DocumentV2 : IJsonMigrationTracked,
    IMigrateFrom<DocumentV1, DocumentV2>
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;

    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(DocumentV1 source, out DocumentV2 result)
    {
        result = new DocumentV2
        {
            Title = source.Title,
            Body = source.Body,
            Slug = source.Title.ToLowerInvariant().Replace(' ', '-'),
        };
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ReadMigrateWriteBackSample.cs#L6-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-read_migrate_write_back_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: read_migrate_write_back_usage -->
<a id='snippet-read_migrate_write_back_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Step 1: Read — old JSON from database/cache/file
var storedJson = """{"$type":"document-v1","title":"Hello World","body":"Content here"}""";

// Step 2: Deserialize — migration happens automatically
var doc = JsonSerializer.Deserialize<DocumentV2>(storedJson, options);

// Step 3: Check if migration occurred
if (doc is { MigratedDuringDeserialization: true })
{
    // Step 4: Write back — re-serialize in the current format
    var updatedJson = JsonSerializer.Serialize(doc, options);

    // Save updatedJson back to the database/cache/file.
    // Future reads won't need migration, improving performance.
    // updatedJson: {"$type":"document-v2","title":"Hello World",...}
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ReadMigrateWriteBackSample.cs#L36-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-read_migrate_write_back_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This pattern is especially valuable for frequently-read data (database records, cache entries). Re-persisting after migration means subsequent reads skip the migration path entirely, improving both latency and allocation.

## Batch migration of NDJSON streams (.NET 11)

Newline-delimited JSON (one record per line) is common for exports, event logs and queue dumps. `DeserializeAsyncEnumerable` with `topLevelValues: true` reads one record at a time and migrates it on the way in; `SerializeAsyncEnumerable` (new in .NET 11) writes the migrated records back in the same format without buffering the whole batch:

<!-- snippet: ndjson_batch_types -->
<a id='snippet-ndjson_batch_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "event-v1")]
public record class EventV1(string Name, string When);

[JsonMigratable(TypeDiscriminator = "event-v2")]
public record class EventV2(string Name, DateTimeOffset OccurredAt) : IJsonMigrationTracked,
    IMigrateFrom<EventV1, EventV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(EventV1 source, out EventV2 result)
    {
        result = new EventV2(source.Name, DateTimeOffset.Parse(source.When, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NdjsonBatchSample.cs#L6-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-ndjson_batch_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: ndjson_batch_usage -->
<a id='snippet-ndjson_batch_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// topLevelValues: true reads newline-delimited JSON one record at a time. Each record is
// migrated as it is read, so the write-back stream only ever contains the current format.
var migratedCount = 0;
var records = JsonSerializer.DeserializeAsyncEnumerable<EventV2>(input, topLevelValues: true, options: options, cancellationToken: cancellationToken);

await JsonSerializer.SerializeAsyncEnumerable(output, TrackMigrations(records), topLevelValues: true, options: options, cancellationToken: cancellationToken);

async IAsyncEnumerable<EventV2> TrackMigrations(IAsyncEnumerable<EventV2?> source)
{
    await foreach (var record in source)
    {
        if (record is null)
        {
            continue;
        }

        if (record.MigratedDuringDeserialization)
        {
            migratedCount++;
        }

        yield return record;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NdjsonBatchSample.cs#L39-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-ndjson_batch_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Records are processed lazily. A record that cannot be migrated throws a `JsonException` from the enumeration; because the reader parses a buffer at a time, records that precede it in the same buffer may not have been yielded yet, so treat the output written so far as partial. Pass the cancellation token to both calls; cancellation surfaces as `OperationCanceledException` from the enumeration.
