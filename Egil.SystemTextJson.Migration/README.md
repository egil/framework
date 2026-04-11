# Egil.SystemTextJson.Migration

[![NuGet](https://img.shields.io/nuget/v/Egil.SystemTextJson.Migration.svg)](https://www.nuget.org/packages/Egil.SystemTextJson.Migration)

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

> **📖 Looking for more?** See the [Recipes](https://github.com/egil/framework/tree/main/Egil.SystemTextJson.Migration/docs/recipes/) for 39 scenario-driven guides covering nested objects, collections, DI, source generation, failure handling, ASP.NET Core, Orleans, telemetry, and more.

## Examples

The examples below use these shared types as a running scenario — a `User` type whose schema has changed between versions:

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


### Static migration

When the migration logic naturally belongs on the target type, implement `IMigrateFrom` directly:

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

Enable migration support on the serializer options and deserialize as usual:

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


### External migration

When migration logic should live in its own class — for separation of concerns, testability, or because you don't control the target type — implement `IMigrate` and register it:

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


### Multi-step migration chains

A target type can accept payloads from multiple older versions. Each source version has its own migration path:

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


### Dependency injection for migrators

Pass an `IServiceProvider` so external migrators can receive constructor-injected dependencies. The migrator is resolved from the service provider on each call, supporting scoped lifetimes:

<!-- snippet: di_migrators -->
<a id='snippet-di_migrators'></a>
```cs
var services = new ServiceCollection();
services.AddScoped<UserMigrator>();

using var serviceProvider = services.BuildServiceProvider();

// When building serializer options, pass the service provider:
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(serviceProvider, builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/DiMigratorsSample.cs#L26-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-di_migrators' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If no service provider is configured, the library falls back to creating the migrator via its parameterless constructor.

### Assembly scanning

Register all `IMigrate<,>` implementations in one or more assemblies instead of listing each one:

<!-- snippet: assembly_scanning -->
<a id='snippet-assembly_scanning'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigratorsFromAssemblies(typeof(UserMigrator).Assembly);
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/AssemblyScanningSample.cs#L24-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-assembly_scanning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Source-generated `JsonSerializerContext`

For AOT scenarios, register both old and current types in a source-generated context:

<!-- snippet: source_gen_context -->
<a id='snippet-source_gen_context'></a>
```cs
[JsonSerializable(typeof(UserV1))]
[JsonSerializable(typeof(UserV2))]
public partial class AppJsonContext : JsonSerializerContext;
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/SourceGenSample.cs#L18-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-source_gen_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: source_gen_usage -->
<a id='snippet-source_gen_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Add(AppJsonContext.Default);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/SourceGenSample.cs#L29-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-source_gen_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Migration tracking

Implement `IJsonMigrationTracked` on your type to detect at runtime whether a particular instance was migrated during deserialization. This is useful for deciding whether to write the value back in its updated form:

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


### Custom type discriminator

By default the library uses `"$type"` as the discriminator property name and the type's full name as its value. Both can be customized:

<!-- snippet: custom_discriminator_attribute -->
<a id='snippet-custom_discriminator_attribute'></a>
```cs
// Per-type via the attribute:
[JsonMigratable(
    TypeDiscriminator = "user-v2",
    TypeDiscriminatorPropertyName = "version")]
public record UserV2(string FirstName, string LastName, int Age);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-custom_discriminator_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: custom_discriminator_builder -->
<a id='snippet-custom_discriminator_builder'></a>
```cs
// Or set a global default property name via the builder:
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.SetTypeDiscriminatorPropertyName("_schema");
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L28-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-custom_discriminator_builder' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also derive the discriminator value from an existing attribute on your types, keeping the library out of your domain model:

<!-- snippet: derive_discriminator -->
<a id='snippet-derive_discriminator'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.GetTypeDiscriminatorFrom<SchemaVersionAttribute>(
        attr => attr.Version);
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L59-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-derive_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Failure handling

Control what happens when a migrator's `TryMigrateFrom` returns `false`:

| Policy | Behavior |
|--------|----------|
| `ThrowJsonException` | Throw a `JsonException` (default). |
| `FallBackToTargetType` | Deserialize the payload directly as the target type. |
| `ReturnNull` | Return `null` (only valid for nullable target types). |

Set a global policy on the builder, or override per-type on the attribute:

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

<!-- snippet: failure_handling_return_null -->
<a id='snippet-failure_handling_return_null'></a>
```cs
// Per-type override:
[JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record OptionalData(string Value);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/FailureHandlingSample.cs#L17-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-failure_handling_return_null' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Observability

The library emits an [OpenTelemetry](https://opentelemetry.io/)-compatible counter (`stjm.migrations`) via `System.Diagnostics.Metrics`. Each migration attempt records the source type, target type, and status (`success` / `failure`).

Subscribe to the meter in a console or test app:

<!-- snippet: otel_meter_listener -->
<a id='snippet-otel_meter_listener'></a>
```cs
// Subscribe to the migration meter using MeterListener:
using var meterListener = new MeterListener();
var migrationCount = 0L;
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == JsonMigrationTelemetry.MeterName)
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meterListener.SetMeasurementEventCallback<long>(
    (instrument, measurement, tags, state) =>
    {
        if (instrument.Name == JsonMigrationTelemetry.MigrationCounterName)
        {
            Interlocked.Add(ref migrationCount, measurement);
        }
    });
meterListener.Start();
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/TelemetrySample.cs#L24-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-otel_meter_listener' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In ASP.NET Core, register the meter with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(JsonMigrationTelemetry.MeterName);
    });
```

### Legacy payloads without a discriminator

Payloads that were serialized before migration support was added will have no `$type` property. The library treats these as **legacy payloads** and attempts migration using the registered source types. This means you can adopt the library incrementally — existing stored JSON keeps working.

## Performance

Every benchmark compares the library against hand-written migration code on top of plain `System.Text.Json`. Each scenario is tested at three payload sizes (2, 32, and 256 array items) to show how overhead scales.

**Key takeaways:**

- **Happy path (no migration needed):** deserialization is ~1.0–1.3× plain STJ with **zero extra allocations**. The overhead comes from the O(1) first-property discriminator check and is constant regardless of payload size.
- **Migration path:** 1.4–1.5× plain STJ for small payloads, converging toward ~1.0× as payload size grows — the fixed migration overhead is amortized over more data.
- **Legacy payloads (no discriminator):** 1.0–1.2× plain STJ with **zero extra allocations** — the same as current-version payloads.
- **Serialization:** near 1:1 at larger payloads (ratio ≈ 1.0). Small payloads show ~2× due to the fixed cost of writing the discriminator property.

Detailed results with source-generated `JsonSerializerContext`:

<!-- This is a summary; see [full source-gen results](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/source-gen-benchmarks.md) and [full reflection results](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/reflection-benchmarks.md). -->

<!-- perf-summary:start -->
| Scenario | TagCount | Ratio vs plain STJ | Alloc Ratio |
|----------|:--------:|:-------------------:|:-----------:|
| **No migration (happy path)** | 2 | 1.25× | 1.00 |
|  | 32 | 0.76× | 1.00 |
|  | 256 | 1.02× | 1.00 |
| **Static migration** | 2 | 1.43× | 1.13 |
|  | 32 | 1.22× | 1.04 |
|  | 256 | 1.06× | 1.01 |
| **External migration** | 2 | 1.50× | 1.13 |
|  | 32 | 1.18× | 1.05 |
|  | 256 | 1.04× | 1.01 |
| **Legacy payload** | 2 | 1.16× | 1.00 |
|  | 32 | 1.05× | 1.00 |
|  | 256 | 0.82× | 1.00 |
| **Serialization** | 2 | 2.10× | 5.45 |
|  | 32 | 1.17× | 2.02 |
|  | 256 | 0.93× | 1.15 |
<!-- perf-summary:end -->

> Full benchmark reports: [source-gen](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/source-gen-benchmarks.md) · [reflection](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/reflection-benchmarks.md)
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
