# Egil.SystemTextJson.Migration

`Egil.SystemTextJson.Migration` adds version-tolerant migration support on top of `System.Text.Json`.

When your data model evolves, this library lets you read old JSON payloads and migrate them to the current type automatically during deserialization. The migration infrastructure adds **zero extra allocation** on the happy path (current-version payloads) and keeps serialization overhead near 1:1 with plain `System.Text.Json`.

## Features

- **`[JsonMigratable]` attribute** — marks types as migration-aware with a type discriminator.
- **Static migrations** via `IMigrateFrom<TSource, TTarget>` — target-type-owned, AOT-friendly.
- **External migrations** via `IMigrate<TSource, TTarget>` — separate migrator classes with optional DI.
- **Assembly scanning** — optional bulk registration of migrator types.
- **Source-generated contexts** — full support for `JsonSerializerContext` metadata.
- **Migration tracking** — `IJsonMigrationTracked` reports whether a value was migrated.
- **Failure handling** — configurable behavior when a migrator returns `false`.

## Quick start

### Static migration (target-owned)

Define your old and current types. The current type implements `IMigrateFrom` with the migration logic:

```csharp
[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

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

Enable migration support on your serializer options:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Deserializing a UserV1 payload automatically migrates to UserV2:
UserV2 user = JsonSerializer.Deserialize<UserV2>(oldJson, options)!;
```

### External migration (separate migrator class)

Use `IMigrate<TSource, TTarget>` when migration logic doesn't belong on the target type:

```csharp
public class UserMigrator : IMigrate<UserV1, UserV2>
{
    public bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```

### Source-generated context

Add your types to a `JsonSerializerContext` and chain it onto the options:

```csharp
[JsonSerializable(typeof(UserV1))]
[JsonSerializable(typeof(UserV2))]
public partial class AppJsonContext : JsonSerializerContext;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Add(AppJsonContext.Default);
```

### Dependency injection

Pass an `IServiceProvider` so external migrators can have constructor dependencies:

```csharp
options.AddJsonMigrationSupport(serviceProvider, builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```

### Migration tracking

Implement `IJsonMigrationTracked` on your type to detect at runtime whether a value was migrated:

```csharp
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age) : IJsonMigrationTracked
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }
}
```

### Failure handling

Control what happens when a migrator returns `false`:

```csharp
options.AddJsonMigrationSupport(builder =>
{
    // Global default:
    builder.SetMigrationFailureHandling(JsonMigrationFailureHandling.FallBackToTargetType);
});

// Or per-type via the attribute:
[JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record MyType(...);
```

## Design notes

- **First-property discriminator check.** The converter inspects only the first JSON property for the type discriminator. This keeps inspection O(1) and allocation-free. The library always serializes `$type` as the first property (`Order = int.MinValue`), so round-tripped payloads work correctly. If external JSON has the discriminator in a non-first position, the payload is treated as a legacy (no-discriminator) payload.

- **Static migrators take precedence.** When both a static `IMigrateFrom` contract and an external `IMigrate` registration exist for the same source type, the static contract wins. This keeps behavior deterministic.

- **Short discriminators recommended.** Using short `TypeDiscriminator` values (e.g. `"user-v2"`) instead of the default full type name reduces the `$type` property size and improves serialization/deserialization throughput.

## Mutation testing

Install local tools (or restore if already installed):

```bash
dotnet tool restore
```

Run mutation testing with Stryker.NET:

```bash
dotnet stryker --config-file stryker-config.json -t mtp
```

Reports are written under `StrykerOutput/`.
