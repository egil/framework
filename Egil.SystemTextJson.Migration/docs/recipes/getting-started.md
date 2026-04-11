# Getting Started

## Setting up migration support

Add `AddJsonMigrationSupport()` to your `JsonSerializerOptions` and deserialize your first migrated payload.

<!-- snippet: shared_types -->
<a id='snippet-shared_types'></a>
```cs
// The old shape. Marked [JsonMigratable] so the library writes
// a type discriminator during serialization and recognizes it
// during deserialization.
[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

// The current shape.
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/SharedTypes.cs#L3-L13' title='Snippet source file'>snippet source</a> | <a href='#snippet-shared_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: static_migration_type -->
<a id='snippet-static_migration_type'></a>
```cs
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IMigrateFrom<UserV1, UserV2>
{
    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StaticMigrationSample.cs#L6-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-static_migration_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: static_migration_usage -->
<a id='snippet-static_migration_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// A UserV1 payload is automatically migrated to UserV2:
var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
// user is UserV2 { FirstName = "Jane", LastName = "Doe", Age = 30 }
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/StaticMigrationSample.cs#L25-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-static_migration_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Notes:
- Call `AddJsonMigrationSupport()` once when configuring options — it registers the converter pipeline.
- The library inspects the first JSON property for a `$type` discriminator. Payloads without one are treated as the target type directly.
- Works with both reflection-based and source-generated `JsonSerializerContext`.

## Choosing between static and external migration

Use **static migration** (`IMigrateFrom<TSource, TTarget>`) when the target type owns the migration logic. Use **external migration** (`IMigrate<TSource, TTarget>`) when you don't own the target type, need constructor-injected dependencies, or want to keep migration logic in a separate class for testability.

| Criteria | Static (`IMigrateFrom`) | External (`IMigrate`) |
|---|---|---|
| Defined on | Target type itself | Separate migrator class |
| DI support | No (static method) | Yes (instance method, resolved from `IServiceProvider`) |
| Discoverability | Automatic (interface on target) | Requires explicit registration or assembly scanning |
| Best for | Simple, self-contained types | Cross-cutting concerns, shared migrators, third-party types |
| Precedence | Wins over external when both exist | Used only if no static migrator exists |

When both a static and external migrator exist for the same source→target pair, the **static migrator always takes precedence**.
