# Egil.SystemTextJson.Migration

Version-tolerant JSON migration for `System.Text.Json`.

When data models evolve, old JSON payloads still exist — in databases, caches, queues, and on disk. This library migrates those payloads to the current type **automatically during deserialization**, so application code never deals with obsolete shapes.

**Key characteristics:**

- **Zero allocation on the happy path** — current-version payloads deserialize with no extra overhead.
- **O(1) discriminator check** — only the first JSON property is inspected to determine the payload version.
- **AOT-friendly** — works with source-generated `JsonSerializerContext`.
- **Two migration styles** — static (target-owned) via `IMigrateFrom<TSource, TTarget>`, or external (separate class) via `IMigrate<TSource, TTarget>` with optional dependency injection.
- **Nested migration** — migratable child types inside migratable parents are migrated recursively.
- **Migration tracking** — types can implement `IJsonMigrationTracked` to know whether they were migrated.
- **Configurable failure handling** — choose between throwing, falling back to the target type, or returning null when a migrator cannot convert a payload.

## Examples

The examples below use these shared types as a running scenario — a `User` type whose schema has changed between versions:

```csharp
// The old shape. Marked [JsonMigratable] so the library writes
// a type discriminator during serialization and recognizes it
// during deserialization.
[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

// The current shape.
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age);
```

### Static migration

When the migration logic naturally belongs on the target type, implement `IMigrateFrom` directly:

```csharp
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

Enable migration support on the serializer options and deserialize as usual:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// A UserV1 payload is automatically migrated to UserV2:
var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
// user is UserV2 { FirstName = "Jane", LastName = "Doe", Age = 30 }
```

### External migration

When migration logic should live in its own class — for separation of concerns, testability, or because you don't control the target type — implement `IMigrate` and register it:

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

### Multi-step migration chains

A target type can accept payloads from multiple older versions. Each source version has its own migration path:

```csharp
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

### Dependency injection for migrators

Pass an `IServiceProvider` so external migrators can receive constructor-injected dependencies. The migrator is resolved from the service provider on each call, supporting scoped lifetimes:

```csharp
services.AddScoped<UserMigrator>();

// Later, when building serializer options:
options.AddJsonMigrationSupport(serviceProvider, builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```

If no service provider is configured, the library falls back to creating the migrator via its parameterless constructor.

### Assembly scanning

Register all `IMigrate<,>` implementations in one or more assemblies instead of listing each one:

```csharp
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigratorsFromAssemblies(typeof(Program).Assembly);
});
```

### Source-generated `JsonSerializerContext`

For AOT scenarios, register both old and current types in a source-generated context:

```csharp
[JsonSerializable(typeof(UserV1))]
[JsonSerializable(typeof(UserV2))]
public partial class AppJsonContext : JsonSerializerContext;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Add(AppJsonContext.Default);
```

### Migration tracking

Implement `IJsonMigrationTracked` on your type to detect at runtime whether a particular instance was migrated during deserialization. This is useful for deciding whether to write the value back in its updated form:

```csharp
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age) : IJsonMigrationTracked
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }
}

// After deserialization:
UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
if (user.MigratedDuringDeserialization)
{
    // Persist the updated representation so future reads
    // hit the happy path.
    await SaveAsync(user);
}
```

### Custom type discriminator

By default the library uses `"$type"` as the discriminator property name and the type's full name as its value. Both can be customized:

```csharp
// Per-type via the attribute:
[JsonMigratable(
    TypeDiscriminator = "user-v2",
    TypeDiscriminatorPropertyName = "version")]
public record UserV2(string FirstName, string LastName, int Age);

// Or set a global default property name via the builder:
options.AddJsonMigrationSupport(builder =>
{
    builder.SetTypeDiscriminatorPropertyName("_schema");
});
```

You can also derive the discriminator value from an existing attribute on your types, keeping the library out of your domain model:

```csharp
options.AddJsonMigrationSupport(builder =>
{
    builder.GetTypeDiscriminatorFrom<SchemaVersionAttribute>(attr => attr.Version);
});
```

### Failure handling

Control what happens when a migrator's `TryMigrateFrom` returns `false`:

| Policy | Behavior |
|--------|----------|
| `ThrowJsonException` | Throw a `JsonException` (default). |
| `FallBackToTargetType` | Deserialize the payload directly as the target type. |
| `ReturnNull` | Return `null` (only valid for nullable target types). |

Set a global policy on the builder, or override per-type on the attribute:

```csharp
options.AddJsonMigrationSupport(builder =>
{
    builder.SetMigrationFailureHandling(
        JsonMigrationFailureHandling.FallBackToTargetType);
});

// Per-type override:
[JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record OptionalData(string Value);
```

### Legacy payloads without a discriminator

Payloads that were serialized before migration support was added will have no `$type` property. The library treats these as **legacy payloads** and attempts migration using the registered source types. This means you can adopt the library incrementally — existing stored JSON keeps working.

## Performance

Every benchmark compares the library against hand-written migration code on top of plain `System.Text.Json`. Each scenario is tested at three payload sizes (2, 32, and 256 array items) to show how overhead scales.

**Key takeaways:**

- **Happy path (no migration needed):** deserialization is ~1.4–2× plain STJ with **zero extra allocations**. The overhead comes from the O(1) first-property discriminator check and is constant regardless of payload size.
- **Migration path:** 1.8–2.7× plain STJ for small payloads, converging toward ~1.8× as payload size grows — the fixed migration overhead is amortized over more data.
- **Legacy payloads (no discriminator):** 1.4–1.8× plain STJ with **zero extra allocations** — the same as current-version payloads.
- **Serialization:** near 1:1 at larger payloads (ratio ≈ 1.0). Small payloads show ~2× due to the fixed cost of writing the discriminator property.

Detailed results with source-generated `JsonSerializerContext`:

<!-- This is a summary; see [full source-gen results](docs/perf/source-gen-benchmarks.md) and [full reflection results](docs/perf/reflection-benchmarks.md). -->

| Scenario | TagCount | Ratio vs plain STJ | Alloc Ratio |
|----------|:--------:|:-------------------:|:-----------:|
| **No migration (happy path)** | 2 | 1.95× | 1.00 |
| | 32 | 1.56× | 1.00 |
| | 256 | 1.39× | 1.00 |
| **Static migration** | 2 | 2.73× | 1.13 |
| | 32 | 2.12× | 1.04 |
| | 256 | 1.83× | 1.01 |
| **External migration** | 2 | 2.70× | 1.13 |
| | 32 | 2.14× | 1.05 |
| | 256 | 1.89× | 1.01 |
| **Legacy payload** | 2 | 1.75× | 1.00 |
| | 32 | 1.54× | 1.00 |
| | 256 | 1.44× | 1.00 |
| **Serialization** | 2 | 2.15× | 5.45 |
| | 32 | 1.21× | 2.02 |
| | 256 | 0.99× | 1.15 |

> Full benchmark reports: [source-gen](docs/perf/source-gen-benchmarks.md) · [reflection](docs/perf/reflection-benchmarks.md)
>
> Run benchmarks locally with `dotnet run --project perf/Egil.SystemTextJson.Migration.PerfTests -c Release`.

## Design notes

- **First-property discriminator check.** The converter inspects only the first JSON property for the type discriminator, keeping detection O(1) and allocation-free. The library serializes `$type` with `Order = int.MinValue` so round-tripped payloads always have it first. If external JSON has the discriminator in a non-first position, the payload is treated as a legacy payload.

- **Static migrators take precedence.** When both a static `IMigrateFrom` and an external `IMigrate` exist for the same source type, the static contract wins.

- **Short discriminators recommended.** Values like `"user-v2"` are smaller and faster to compare than the default full type name.

## Mutation testing

```bash
dotnet tool restore
dotnet stryker --config-file stryker-config.json -t mtp
```

Reports are written under `StrykerOutput/`.
