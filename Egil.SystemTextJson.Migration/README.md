# Egil.SystemTextJson.Migration

[![NuGet](https://img.shields.io/nuget/v/Egil.SystemTextJson.Migration.svg)](https://www.nuget.org/packages/Egil.SystemTextJson.Migration)

Version-tolerant JSON migration for `System.Text.Json`.

When data models evolve, old JSON payloads still exist — in databases, caches, queues, and on disk. This library migrates those payloads to the current type **automatically during deserialization**, so application code never deals with obsolete shapes.

**Key characteristics:**

- **Little to no overhead for normal-sized payloads** — the medium source-generated happy-path profile benchmarks close to plain `System.Text.Json` throughput with zero extra library allocations.
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


### Choosing a migration contract

Use the contract that matches where the migration logic lives:

| Scenario | Interface | Registration |
|----------|-----------|--------------|
| The current `[JsonMigratable]` target type owns the migration logic | `IMigrateFrom<TSource, TTarget>` | No `RegisterMigrator*` call. The target type's contracts are discovered automatically when its converter is created. |
| A separate external class owns the migration logic | `IMigrate<TSource, TTarget>` | Register the migrator with `RegisterMigrator*` or `RegisterMigratorsFrom*`. |

Do not implement `IMigrate<TSource, TTarget>` directly on a `[JsonMigratable]` type. That interface is reserved for separate external migrator classes.

### Static migration

When the migration logic naturally belongs on the target type, implement `IMigrateFrom` directly. These target-owned migrations are discovered automatically; no `RegisterMigrator*` or `RegisterMigratorsFrom*` call is needed:

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

### Brownfield adoption

Existing projects can adopt migration support one type at a time:

- **No shape change needed:** add `[JsonMigratable]` to the current type and enable `AddJsonMigrationSupport()`. Existing discriminator-less object payloads that already match the current type continue to deserialize as that type; new writes include `$type`, so future reads use the normal happy path.
- **Existing payloads need migration:** if stored JSON was written before `[JsonMigratable]` existed and represents an older object shape, set `UndiscriminatedSourceType` on the current type and provide a static or external migrator from that source type. The library then treats discriminator-less object payloads as that source shape, while discriminator-bearing payloads still use normal version matching.

When stored JSON represents an older source shape, configure the target with `UndiscriminatedSourceType`:

<!-- snippet: legacy_undiscriminated_source_type -->
<a id='snippet-legacy_undiscriminated_source_type'></a>
```cs
[JsonMigratable(
    TypeDiscriminator = "customer-name-v1",
    UndiscriminatedSourceType = typeof(CustomerNameV0))]
public record class CustomerNameV1(string Name)
    : IMigrateFrom<CustomerNameV0, CustomerNameV1>
{
    public static bool TryMigrateFrom(CustomerNameV0 source, out CustomerNameV1 result)
    {
        result = new CustomerNameV1($"{source.FirstName} {source.LastName}");
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L19-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_undiscriminated_source_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: legacy_undiscriminated_source_usage -->
<a id='snippet-legacy_undiscriminated_source_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Existing stored JSON was written before migration support existed,
// so it has no $type discriminator. CustomerNameV1 opts in to treating
// discriminator-less objects as CustomerNameV0 and runs its migrator.
var json = """{"firstName":"Jane","lastName":"Doe"}""";

CustomerNameV1 customer = JsonSerializer.Deserialize<CustomerNameV1>(json, options)!;
// customer is CustomerNameV1 { Name = "Jane Doe" }
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L79-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_undiscriminated_source_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`UndiscriminatedSourceType` is intentionally one source type per target. If multiple historical object shapes exist without discriminators, choose the one that represents the stored brownfield payloads you need to migrate.

### External migration

When migration logic should live in its own separate class — for separation of concerns, testability, dependency injection, or because you don't control the target type — implement `IMigrate` and register it:

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


### Migrating from non-object JSON payloads

When the stored JSON is not an object — for example, a plain array or a primitive — the library can migrate it to a structured target type. The source type does not need `[JsonMigratable]`:

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

After migration, the target type serializes as an object with `$type`, so future reads take the zero-allocation happy path. See the [recipe](https://github.com/egil/framework/tree/main/Egil.SystemTextJson.Migration/docs/recipes/migration-authoring.md#migrating-from-non-object-json-payloads) for more examples including primitives and mixed migrators.


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

Register external `IMigrate<,>` migrator classes in one or more assemblies instead of listing each one. Assembly scanning is not required for target-owned `IMigrateFrom<,>` migrations:

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

## Performance

Every benchmark compares the library against hand-written migration code on top of plain `System.Text.Json`. The small profile is a minimal `{ "name": "...", "age": ... }` object that highlights worst-case fixed overhead; the medium profile is a best-guess average object with about 12 object members; and the large profile has about 96 object members spread across nested objects, arrays, and dictionary entries. See [benchmark payload examples](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/payload-examples.md) for representative JSON from each profile.

The generated table below is refreshed by `.\scripts\update-perf-docs.ps1` from the latest source-generated BenchmarkDotNet report. It keeps BenchmarkDotNet's `Ratio`, `RatioSD`, and `Alloc Ratio` columns so README numbers stay tied to the raw benchmark output.

<!-- This is a summary; see [full source-gen results](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/source-gen-benchmarks.md) and [full reflection results](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/reflection-benchmarks.md). -->

<!-- perf-summary:start -->
| Scenario | Method | Payload size | Mean | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------|--------|:------------:|-----:|------:|--------:|----------:|------------:|
| **No migration (happy path)** | Plain STJ | Small | 304.5 ns | 1.00 | 0.06 | 160 B | 1.00 |
|  | JsonMigratable | Small | 337.1 ns | 1.11 | 0.05 | 160 B | 1.00 |
|  | Plain STJ | Medium | 1,465.9 ns | 1.00 | 0.01 | 1656 B | 1.00 |
|  | JsonMigratable | Medium | 1,572.8 ns | 1.07 | 0.01 | 1656 B | 1.00 |
|  | Plain STJ | Large | 15,625.2 ns | 1.00 | 0.01 | 24624 B | 1.00 |
|  | JsonMigratable | Large | 16,915.7 ns | 1.08 | 0.10 | 24624 B | 1.00 |
| **Static migration** | Manual STJ migration | Small | 342.7 ns | 1.00 | 0.08 | 312 B | 1.00 |
|  | JsonMigratable | Small | 632.9 ns | 1.85 | 0.19 | 312 B | 1.00 |
|  | Manual STJ migration | Medium | 2,013.6 ns | 1.00 | 0.06 | 1808 B | 1.00 |
|  | JsonMigratable | Medium | 2,104.1 ns | 1.05 | 0.05 | 1808 B | 1.00 |
|  | Manual STJ migration | Large | 17,840.2 ns | 1.00 | 0.10 | 24776 B | 1.00 |
|  | JsonMigratable | Large | 16,988.8 ns | 0.96 | 0.09 | 24776 B | 1.00 |
| **External migration** | Manual STJ migration | Small | 334.1 ns | 1.00 | 0.06 | 312 B | 1.00 |
|  | JsonMigratable | Small | 566.9 ns | 1.70 | 0.08 | 312 B | 1.00 |
|  | Manual STJ migration | Medium | 1,861.7 ns | 1.00 | 0.06 | 1808 B | 1.00 |
|  | JsonMigratable | Medium | 2,405.9 ns | 1.29 | 0.08 | 1808 B | 1.00 |
|  | Manual STJ migration | Large | 17,206.9 ns | 1.01 | 0.13 | 24776 B | 1.00 |
|  | JsonMigratable | Large | 18,846.3 ns | 1.10 | 0.17 | 24776 B | 1.00 |
| **Undiscriminated source migration** | Manual STJ migration | Small | 379.1 ns | 1.00 | 0.09 | 312 B | 1.00 |
|  | JsonMigratable | Small | 491.2 ns | 1.30 | 0.11 | 312 B | 1.00 |
|  | Manual STJ migration | Medium | 2,026.8 ns | 1.00 | 0.08 | 1808 B | 1.00 |
|  | JsonMigratable | Medium | 2,106.4 ns | 1.04 | 0.10 | 1808 B | 1.00 |
|  | Manual STJ migration | Large | 16,685.8 ns | 1.03 | 0.25 | 24776 B | 1.00 |
|  | JsonMigratable | Large | 15,659.2 ns | 0.97 | 0.18 | 24776 B | 1.00 |
| **Legacy payload** | Plain STJ + tracking | Small | 373.4 ns | 1.00 | 0.04 | 192 B | 1.00 |
|  | JsonMigratable | Small | 486.0 ns | 1.30 | 0.08 | 192 B | 1.00 |
|  | Plain STJ + tracking | Medium | 1,933.9 ns | 1.01 | 0.12 | 1688 B | 1.00 |
|  | JsonMigratable | Medium | 1,991.9 ns | 1.04 | 0.12 | 1688 B | 1.00 |
|  | Plain STJ + tracking | Large | 16,924.1 ns | 1.00 | 0.02 | 24656 B | 1.00 |
|  | JsonMigratable | Large | 15,528.3 ns | 0.92 | 0.05 | 24656 B | 1.00 |
| **Serialization** | Plain STJ | Small | 107.8 ns | 1.00 | 0.03 | 56 B | 1.00 |
|  | JsonMigratable | Small | 169.8 ns | 1.58 | 0.03 | 136 B | 2.43 |
|  | Plain STJ | Medium | 436.8 ns | 1.00 | 0.00 | 416 B | 1.00 |
|  | JsonMigratable | Medium | 735.2 ns | 1.68 | 0.01 | 800 B | 1.92 |
|  | Plain STJ | Large | 3,689.9 ns | 1.00 | 0.01 | 10384 B | 1.00 |
|  | JsonMigratable | Large | 4,982.6 ns | 1.35 | 0.01 | 10776 B | 1.04 |
<!-- perf-summary:end -->

> Full benchmark reports: [source-gen](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/source-gen-benchmarks.md) · [reflection](https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/perf/reflection-benchmarks.md)
>
> Run benchmarks locally with `dotnet run --project perf/Egil.SystemTextJson.Migration.PerfTests -c Release`.
> Refresh these docs from the latest BenchmarkDotNet output with `.\scripts\update-perf-docs.ps1`.

## Design notes

- **First-property discriminator check.** The converter inspects only the first JSON property for the type discriminator, keeping detection O(1) and allocation-free. The library serializes `$type` with `Order = int.MinValue` so round-tripped payloads always have it first. If external JSON has the discriminator in a non-first position, the payload is treated as a legacy payload.

- **Static migrators take precedence.** When both a static `IMigrateFrom` and an external `IMigrate` exist for the same source type, the static contract wins.

- **Short discriminators recommended.** Values like `"user-v2"` are smaller and faster to compare than the default full type name.

- **Non-object payload migration.** When the JSON payload is not an object (e.g., an array or primitive), discriminator-based matching is not possible. The library matches migrators by comparing the JSON token type against the source type's `JsonTypeInfoKind` (`StartArray` → `Enumerable`, primitives → `None`). Dictionary source types (`Dictionary<string, T>`) are also supported — when no discriminator match is found on a JSON object, the library checks for `JsonTypeInfoKind.Dictionary` migrators before falling back to legacy handling. This adds zero overhead to the existing object-based happy path.

## Mutation testing

```bash
dotnet tool restore
dotnet stryker --config-file stryker-config.json -t mtp
```

Reports are written under `StrykerOutput/`.
