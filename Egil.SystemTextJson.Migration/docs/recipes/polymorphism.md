# Polymorphism (`[JsonPolymorphic]`) and Unions

This page documents a current limitation — **`[JsonMigratable]` cannot be combined with `[JsonPolymorphic]` / `[JsonDerivedType]` on the same type hierarchy** — and the .NET 11 alternative: a C# `union` whose cases are `[JsonMigratable]` types.

## Summary

| Scenario | Supported? |
|----------|------------|
| `[JsonPolymorphic]` on a base type with `[JsonDerivedType]` entries (no `[JsonMigratable]` anywhere in the hierarchy) | ✅ Works — uses System.Text.Json's built-in polymorphism |
| `[JsonMigratable]` on a non-polymorphic type | ✅ Works — this is the library's primary scenario |
| `[JsonMigratable]` on a `[JsonPolymorphic]` base type | ❌ Throws `NotSupportedException` at type-info configuration time |
| `[JsonPolymorphic]` on a base, `[JsonMigratable]` on a derived `[JsonDerivedType]` entry | ❌ Throws `NotSupportedException` at deserialization time — also with a `JsonTypeClassifier` on .NET 11 |
| C# `union` whose cases are `[JsonMigratable]` types (.NET 11) | ✅ Works — `AddJsonMigrationSupport()` classifies the union by migration discriminator |
| `[JsonMigratable]` on the `union` itself | ❌ Throws `NotSupportedException` with guidance — annotate the case types instead |

## Why `[JsonPolymorphic]` doesn't work

System.Text.Json's polymorphic infrastructure requires every converter in a polymorphic hierarchy to support the internal **metadata protocol** — the read-ahead state machine that handles the `$type` discriminator property.

The gating check is the `JsonConverter.CanHaveMetadata` property:

```csharp
// In System.Text.Json (internal):
internal virtual bool CanHaveMetadata => false;
```

This property is `internal virtual`. It defaults to `false` for all custom `JsonConverter<T>` implementations and is overridden to `true` only by built-in converters such as `ObjectDefaultConverter<T>`. There is no public extensibility point for it (see [dotnet/runtime#118900](https://github.com/dotnet/runtime/issues/118900) for the open API proposal to expose it).

When STJ configures a polymorphic type, it calls into `PolymorphicTypeResolver`, which inspects `Converter.CanHaveMetadata`:

```csharp
// In PolymorphicTypeResolver constructor:
if (UsesTypeDiscriminators && !converterCanHaveMetadata)
{
    ThrowHelper.ThrowNotSupportedException_BaseConverterDoesNotSupportMetadata(BaseType);
}
```

Because `JsonMigratableConverter<T>` is a custom `JsonConverter<T>`, it inherits `CanHaveMetadata == false`. That trips the check above the moment the polymorphic type info is built.

## What you'll see

```
System.NotSupportedException: The converter for derived type 'YourType'
does not support metadata writes or reads.
```

The message names the base type when `[JsonMigratable]` is on the polymorphic base (thrown at type-info configuration time) and the derived type when `[JsonMigratable]` is on a derived `[JsonDerivedType]` entry (thrown at serialization/deserialization time).

## .NET 11 status

.NET 11 ships `JsonTypeClassifier`: a delegate that inspects a copy of the reader and returns the CLR type to deserialize. It can be attached to polymorphic types (`[JsonPolymorphic(TypeClassifier = ...)]`) and to unions (`[JsonUnion(TypeClassifier = ...)]` or `JsonSerializerOptions.TypeClassifiers`).

For **`[JsonPolymorphic]` hierarchies the classifier does not lift the limitation**. STJ still checks `CanHaveMetadata` on the derived converter it resolves through the classifier, so a `[JsonMigratable]` derived type fails with the same `NotSupportedException`. Until [dotnet/runtime#118900](https://github.com/dotnet/runtime/issues/118900) ships, keep using one of the workarounds below.

For **C# unions there is no metadata gate**: the union converter classifies the payload and then hands the reader to the selected case's converter, custom or not. This library uses that seam.

### Recommended on .NET 11: model the hierarchy as a union

Annotate the case types with `[JsonMigratable]` exactly as you would for standalone types, and declare the union over the current versions:

<!-- snippet: union_migration_types -->
<a id='snippet-union_migration_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "circle-v1")]
public record class CircleV1(double R);

[JsonMigratable(TypeDiscriminator = "circle-v2")]
public record class CircleV2(double Radius) : IMigrateFrom<CircleV1, CircleV2>
{
    public static bool TryMigrateFrom(CircleV1 source, out CircleV2 result)
    {
        result = new CircleV2(source.R);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "rectangle")]
public record class Rectangle(double Width, double Height);

// Each case carries its own [JsonMigratable] discriminator; the union itself needs no attribute
// when the options come from AddJsonMigrationSupport().
public union Shape(CircleV2, Rectangle);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/UnionMigrationSample.cs#L4-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-union_migration_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`AddJsonMigrationSupport()` registers `JsonMigratableUnionTypeClassifier` in `JsonSerializerOptions.TypeClassifiers`, so reflection-based serialization needs nothing else:

<!-- snippet: union_migration_usage -->
<a id='snippet-union_migration_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Current payloads route to the case that owns the discriminator...
var rectangle = JsonSerializer.Deserialize<Shape>("""{"$type":"rectangle","width":2,"height":3}""", options);

// ...and old payloads route to the case that migrates them.
var circle = JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v1","r":1.5}""", options);

// Writing a union writes the active case, including its discriminator.
var json = JsonSerializer.Serialize(circle, options);
// json: {"$type":"circle-v2","radius":1.5}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/UnionMigrationSample.cs#L43-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-union_migration_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Source-generated contexts must name the classifier on the union, because the generator rejects unions whose cases share a JSON value type unless a classifier is declared (`SYSLIB1227`). The same type works for both routes:

<!-- snippet: union_migration_source_gen_types -->
<a id='snippet-union_migration_source_gen_types'></a>
```cs
// Source-generated contexts must name the classifier on the union, because the generator
// rejects unions whose cases share a JSON value type unless a classifier is declared.
[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]
public union GeneratedShape(CircleV2, Rectangle);

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GeneratedShape))]
[JsonSerializable(typeof(CircleV1))]
public partial class ShapeJsonContext : JsonSerializerContext;
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/UnionMigrationSample.cs#L26-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-union_migration_source_gen_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: union_migration_source_gen_usage -->
<a id='snippet-union_migration_source_gen_usage'></a>
```cs
var options = new JsonSerializerOptions(ShapeJsonContext.Default.Options);
options.AddJsonMigrationSupport();

var shape = JsonSerializer.Deserialize<GeneratedShape>("""{"$type":"circle-v1","R":1.5}""", options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/UnionMigrationSample.cs#L67-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-union_migration_source_gen_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### How union payloads are classified

The classifier is built once per union and compares pre-encoded UTF-8 bytes on the read path; it does not allocate unless it throws.

1. **Object payloads** are routed by their **first property**. A known discriminator selects the case it belongs to: each `[JsonMigratable]` case claims its own discriminator plus the discriminators of every object source type that migrates into it (static `IMigrateFrom<,>` contracts and registered external migrators). The case's converter then performs the migration as usual, so failure handling, tracking and nested migration all apply.
2. **Guard:** if any case is a nested union, uses a converter override (`[JsonConverter]` on the type or a matching entry in `options.Converters`, such as `JsonStringEnumConverter`) or has a source type with such a converter, that case may accept any JSON shape. The union then classifies discriminated payloads only (an overridden object-like source still contributes its discriminator); every payload without a leading discriminator throws.
3. Otherwise an object without a recognized leading discriminator goes to, in order: the single case that declares `UndiscriminatedSourceType`; the single case whose contract is a JSON object or dictionary, or that migrates from a dictionary source; the single migratable case (legacy-payload semantics).
4. **Arrays, strings, numbers and booleans** go to the single case with that JSON shape, including migratable cases that migrate from a source of that shape (for example `IMigrateFrom<int, Counter>` gives `Counter` the number route). Numbers use the same rules as [non-object payload migration](migration-authoring.md#migrating-from-non-object-json-payloads), and with `JsonNumberHandling.AllowReadingFromString` (on by default with `JsonSerializerDefaults.Web`) a numeric case also takes JSON strings when the union has no string-shaped case; string-shaped types are `string`, `char`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `Uri`, `Version` and `byte[]`.
5. **`null`** never reaches the classifier: STJ yields the nullable case or the default union value.

Any other situation throws a `JsonException` that lists the known discriminators, rather than silently picking a case. Two cases claiming the same discriminator, or two cases configuring `UndiscriminatedSourceType`, throw an `InvalidOperationException` when the union is first used.

> **Note:** Serializing a union writes the active case inline — the case's `$type` is what lands on the wire, no envelope is added. Payloads written by this library therefore always classify on the fast path.

## Workarounds for `[JsonPolymorphic]`

### Option 1 — Use `[JsonMigratable]` discriminator-based dispatch instead of `[JsonPolymorphic]`

`[JsonMigratable]` already provides discriminator-based dispatch — it just calls the property `$type` (or whatever you configure via `TypeDiscriminatorPropertyName`) and the value `TypeDiscriminator`. If your only reason to use `[JsonPolymorphic]` was to dispatch to subtypes by a discriminator, model each subtype as a `[JsonMigratable]` type and migrate between them as needed:

```csharp
[JsonMigratable(TypeDiscriminator = "dog")]
public class Dog { /* ... */ }

[JsonMigratable(TypeDiscriminator = "cat")]
public class Cat { /* ... */ }
```

Deserialize as the concrete type, or on .NET 11 as a `union Pet(Dog, Cat)`. See [type-discriminators.md](type-discriminators.md) for customization options.

### Option 2 — Keep `[JsonPolymorphic]` and migrate at the leaf types only

If you need full STJ polymorphism (for example, you rely on `JsonUnknownDerivedTypeHandling`, source-generator emission, or interop with another tool), keep `[JsonPolymorphic]` on the base type and **do not** put `[JsonMigratable]` anywhere in the hierarchy. Handle version migration outside the polymorphic layer — for example, deserialize the polymorphic payload, then transform old derived types to current ones in your application code.

### Option 3 — Wrap migratable types behind a non-polymorphic boundary

If a class needs to participate in a polymorphic hierarchy and also evolve over time, expose a stable wrapper type to the polymorphic layer and put `[JsonMigratable]` on the wrapper's payload property type instead of the wrapper itself. The polymorphic layer sees only the stable wrapper; the migration layer handles the inner payload.
