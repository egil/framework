# Migration Authoring

## Writing a static migrator

The target type implements `IMigrateFrom<TSource, TTarget>` directly — the simplest approach for a single-version upgrade.

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

> **Note:** The source type needs `[JsonMigratable]` so the library knows its discriminator. The `TryMigrateFrom` method is `static abstract`, keeping the target type's API clean.

## Writing an external migrator

A separate class implements `IMigrate<TSource, TTarget>`. Use this when the target type doesn't own the migration, or when the migrator needs constructor-injected dependencies.

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

<!-- snippet: external_migrator -->
<a id='snippet-external_migrator'></a>
```cs
public class UserMigrator : IMigrate<UserV1, UserV2>
{
    public bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ExternalMigrationSample.cs#L9-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-external_migrator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: external_migration_setup -->
<a id='snippet-external_migration_setup'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ExternalMigrationSample.cs#L26-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-external_migration_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** External migrators must be registered explicitly via `RegisterMigrator<T>()` or discovered via `RegisterMigratorsFromAssembly()`. They support DI via `UseServiceProvider()`.

## Multi-step migration chain

A target type can accept payloads from **multiple older versions**. Implement `IMigrateFrom` for each source type. Each migrator handles one version jump — there is no automatic chaining through intermediate types.

<!-- snippet: multi_step_chain -->
<a id='snippet-multi_step_chain'></a>
```cs
[JsonMigratable(TypeDiscriminator = "user-v0")]
public record UserV0(string FullName);

[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IMigrateFrom<UserV0, UserV2>,
      IMigrateFrom<UserV1, UserV2>
{
    public static bool TryMigrateFrom(UserV0 source, out UserV2 result)
    {
        var names = source.FullName.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", 0);
        return true;
    }

    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/MultiStepChainSample.cs#L3-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-multi_step_chain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Each `IMigrateFrom` implementation migrates directly to the current target type. The library does not chain V0 → V1 → V2 automatically — each migration must produce the final target type.

## Migrating across many versions

When the domain evolves through many versions, you have two strategies:

**Direct migration** (recommended): Each old version migrates directly to the current version. Simpler, no intermediate allocations.

```
V0 ──→ V3  (via IMigrateFrom<V0, V3>)
V1 ──→ V3  (via IMigrateFrom<V1, V3>)
V2 ──→ V3  (via IMigrateFrom<V2, V3>)
```

**Composed migration**: Reuse existing migration logic by calling earlier migrators within later ones. Reduces code duplication at the cost of intermediate object creation.

```csharp
public static bool TryMigrateFrom(V0 source, out V3 result)
{
    // Reuse V0→V1 logic, then V1→V2, then V2→V3
    if (V1.TryMigrateFrom(source, out var v1)
        && V2.TryMigrateFrom(v1, out var v2)
        && V3.TryMigrateFrom(v2, out var v3))
    {
        result = v3;
        return true;
    }
    result = default!;
    return false;
}
```

> **Note:** The library supports both approaches. Choose based on whether code reuse or performance matters more in your scenario.
