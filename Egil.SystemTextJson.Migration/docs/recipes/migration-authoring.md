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

## Migrating from object payloads without discriminators

When adopting migration support for a type that already has stored JSON, older object payloads may not contain a `$type` discriminator. If those payloads should be read as a previous source shape instead of the current target shape, set `UndiscriminatedSourceType` on the target's `[JsonMigratable]` attribute.

The configured source type does not need `[JsonMigratable]`; it only needs JSON metadata and a matching static or registered external migrator to the target. The opt-in names exactly one source type, so targets with several object migrators remain deterministic.

See [Migrating discriminator-less object payloads from a source type](legacy-adoption.md#migrating-discriminator-less-object-payloads-from-a-source-type) for a complete sample.

## Migrating from non-object JSON payloads

When the stored JSON is not an object — for example, a plain array or a primitive value — the library can still migrate it to a structured target type. This is useful when the original data model stored a simple value (like a `List<string>` or a raw `string`) and you later upgraded to a richer type.

The source type does **not** need `[JsonMigratable]` — it's a plain .NET type whose JSON representation is an array, string, number, or boolean.

<!-- snippet: non_object_list_types -->
<a id='snippet-non_object_list_types'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L3-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_list_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: non_object_list_usage -->
<a id='snippet-non_object_list_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Stored JSON is a plain array — no $type, no object wrapper.
var json = """["csharp","dotnet","azure"]""";
SettingsV2 settings = JsonSerializer.Deserialize<SettingsV2>(json, options)!;
// settings.Tags = ["csharp", "dotnet", "azure"], settings.Label = "migrated"
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L81-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_list_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The same approach works for **primitive source types** like `string`, `int`, or `bool`:

<!-- snippet: non_object_string_type -->
<a id='snippet-non_object_string_type'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L19-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_string_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also combine non-object and object-based migrators on the same target type. The library automatically picks the right migrator based on the JSON shape:

<!-- snippet: non_object_mixed_migrators -->
<a id='snippet-non_object_mixed_migrators'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L33-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_mixed_migrators' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Non-object payloads have no type discriminator. The library matches migrators by comparing the JSON token type (`StartArray`, `String`, `Number`, `True`/`False`) against the source type's `JsonTypeInfoKind`. Object payloads continue to use discriminator-based matching. After migration, the target type is serialized as an object with `$type`, so future reads take the zero-allocation happy path.

**Dictionary source types** work the same way. Even though dictionaries serialize as JSON objects, the library detects that no discriminator matched and looks for migrators with `JsonTypeInfoKind.Dictionary`:

<!-- snippet: non_object_dict_type -->
<a id='snippet-non_object_dict_type'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L60-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_dict_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: non_object_dict_usage -->
<a id='snippet-non_object_dict_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Stored JSON is a dictionary — a JSON object with no $type discriminator.
var json = """{"redis":"6379","postgres":"5432"}""";
PortConfigV2 config = JsonSerializer.Deserialize<PortConfigV2>(json, options)!;
// config.Ports = {"redis": "6379", "postgres": "5432"}, config.Version = 2
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NonObjectPayloadMigrationSample.cs#L158-L166' title='Snippet source file'>snippet source</a> | <a href='#snippet-non_object_dict_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Dictionary migrators are checked only after discriminator matching fails. If the target type also has object-based migrators (with discriminators), those take priority for object payloads. Dictionary migrators act as a fallback for unrecognized JSON objects.

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

## Bidirectional migration (blue/green deployments)

In blue/green deployment scenarios two service versions may run side by side, each writing its own schema. Both types must be able to read the other's payloads. The library supports this by allowing **bidirectional** migration — type `X` migrates from `Y` *and* type `Y` migrates from `X` — without triggering a cycle error.

### Static bidirectional migration

Each type implements `IMigrateFrom` for the other:

```csharp
[JsonMigratable(TypeDiscriminator = "order-blue")]
public record OrderBlue(string Name, int Quantity)
    : IMigrateFrom<OrderGreen, OrderBlue>
{
    public static bool TryMigrateFrom(OrderGreen source, out OrderBlue result)
    {
        result = new OrderBlue(source.Label, source.Count);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "order-green")]
public record OrderGreen(string Label, int Count)
    : IMigrateFrom<OrderBlue, OrderGreen>
{
    public static bool TryMigrateFrom(OrderBlue source, out OrderGreen result)
    {
        result = new OrderGreen(source.Name, source.Quantity);
        return true;
    }
}
```

The **blue** service deserializes as `OrderBlue` — payloads tagged `"order-green"` are automatically migrated. The **green** service deserializes as `OrderGreen` — payloads tagged `"order-blue"` are automatically migrated. Payloads that already match the target type are deserialized directly with no migration overhead.

### External bidirectional migration

The same pattern works with external migrator classes:

```csharp
[JsonMigratable(TypeDiscriminator = "order-blue")]
public record OrderBlue(string Name, int Quantity);

[JsonMigratable(TypeDiscriminator = "order-green")]
public record OrderGreen(string Label, int Count);

public class BlueToGreenMigrator : IMigrate<OrderBlue, OrderGreen>
{
    public bool TryMigrateFrom(OrderBlue source, out OrderGreen result)
    {
        result = new OrderGreen(source.Name, source.Quantity);
        return true;
    }
}

public class GreenToBlueMigrator : IMigrate<OrderGreen, OrderBlue>
{
    public bool TryMigrateFrom(OrderGreen source, out OrderBlue result)
    {
        result = new OrderBlue(source.Label, source.Count);
        return true;
    }
}
```

Register both migrators during setup:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigrator<BlueToGreenMigrator>();
    builder.RegisterMigrator<GreenToBlueMigrator>();
});
```

> **Note:** Bidirectional migration is specifically designed for the case where two distinct types reference each other. True cycles through three or more intermediate types (A → B → C → A) are not supported and will still result in an error.
