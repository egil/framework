# Upgrading to 2.0

2.0 changes no public API: every type, method and attribute from 1.x is still there with the same signature. The major version signals changes in behavior. This page lists them, with the check to run against your code and the fix when it applies.

## Stored data

JSON written by 1.x reads the same under 2.0. That rests on three rules that did not change and one that was kept as a fallback:

- Object payloads are routed by their first property, as before: a known discriminator selects the source it belongs to, the target's own discriminator selects the target, and an object without a recognized leading discriminator is a legacy payload unless `UndiscriminatedSourceType` claims it. The discriminator property name, its default value (the type's full name) and its first position in written output are unchanged.
- Array payloads are routed to enumerable sources by contract kind, as before; when several qualify, 2.0 disambiguates cases that 1.x reported as ambiguous.
- Scalar payloads are matched in tiers, and the first tier is 1.x's own rule: a source whose CLR type 1.x matched by `TypeCode` (`string`, `bool`, the primitive numerics, enums) takes the token before any type 2.0 added is considered, and a converter override on such a source only ranks it behind other 1.x sources, not behind 2.0 ones. A source that 1.x selected is therefore still selected, also when a 2.0 source of the same JSON shape (`Guid` next to `string`, `Half` next to `int`) is registered on the same target; a 2.0 source is only selected where 1.x had no match. The one departure is deliberate: where 1.x reported two of its sources as ambiguous because one was behind a converter override, 2.0 picks the plain one. The same order applies to the first element when several collection sources compete.
- Migrators, failure handling, tracking and nested migration run the same code as before.

The 1.7 release's test suite, run unmodified against 2.0, passes every payload test; the one failing test covers the options-setup change described below. Issue #218 tracks running the previous release's suite in CI so this stays true.

## Migration support lives in the resolver chain

`AddJsonMigrationSupport()` used to append a converter factory to `options.Converters`. It now inserts a resolver at the front of `options.TypeInfoResolverChain` and leaves `options.Converters` alone, so types outside migration keep System.Text.Json's source-generated fast-path serializers (see [Keeping the generated fast path](aot-source-gen.md#keeping-the-generated-fast-path)). Two setups behave differently.

**A custom `IJsonTypeInfoResolver` that overrides a `[JsonMigratable]` type.** In 1.x a resolver that returned its own contract for a `[JsonMigratable]` type won silently, because it never consulted `options.Converters`. In 2.0 migration owns the type unless the resolver sits ahead of it in the chain; otherwise the non-object contract is refused with `NotSupportedException` the first time the type is used.

- Check: do you set `TypeInfoResolver` or add to `TypeInfoResolverChain` a resolver that returns contracts for `[JsonMigratable]` types?
- Fix: insert it ahead of migration, after `AddJsonMigrationSupport()`:

```csharp
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Insert(0, new MyResolver());
```

**Replacing the chain after registration.** Assigning `options.TypeInfoResolver = ...` or calling `options.TypeInfoResolverChain.Clear()` after `AddJsonMigrationSupport()` removes migration support when the new resolver no longer reaches the migration entry, for example a fresh `DefaultJsonTypeInfoResolver` or a context. There is no error: `[JsonMigratable]` types then serialize without `$type` and old payloads deserialize as the current type. In 1.x the converter list carried migration through such changes. Decorating or wrapping the entry is different: `WithAddedModifier`, `JsonTypeInfoResolver.Combine` and an application-defined resolver that forwards to the entry all keep migration working.

- Check: search for `TypeInfoResolver =` and `TypeInfoResolverChain.Clear` after the call to `AddJsonMigrationSupport()`, and confirm the assigned resolver still delegates to the migration entry.
- Fix: call `AddJsonMigrationSupport()` last, or use `TypeInfoResolverChain.Add(...)` to add contexts, which is the order the README shows.

Unchanged: converters in `options.Converters` keep their precedence. One registered before `AddJsonMigrationSupport()` still wins for a `[JsonMigratable]` type; one registered after still does not. Calling `AddJsonMigrationSupport()` twice keeps the first registration, as before. Options without any resolver keep reflection-based serialization.

## Non-object payload matching

1.x matched a scalar payload to a source type by `TypeCode` (`string`, `bool`, the primitive numerics and enums) and a JSON array to any enumerable source. 2.0 matches by the JSON token family the source's converter reads, which widens what can migrate:

- numbers reach `Half`, `Int128`, `UInt128` and, on .NET 11, `BFloat16` and `Decimal32/64/128`;
- strings reach `char`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `Uri`, `Version`, `byte[]`, `Memory<byte>` and `ReadOnlyMemory<byte>`;
- a quoted number reaches a numeric source under `JsonNumberHandling.AllowReadingFromString` (on by default with `JsonSerializerDefaults.Web`) when no string-shaped source exists;
- several collection sources are told apart by the first element's discriminator or contract kind instead of throwing.

Every one of these was a `JsonException` in 1.x, so existing data is unaffected. Matching runs in tiers: 1.x's `TypeCode` sources first (plain ones ahead of ones behind a converter override such as `JsonStringEnumConverter`), then the types listed above, then quoted numbers, then overridden sources of the newer types. Two candidates in one tier are ambiguous and throw, as two 1.x numeric sources always were; a 1.x source and a 2.0 source never compete. Where 1.x reported two of its own sources as ambiguous because one was behind a converter override (an `int` and a string-converted enum both matching a number), 2.0 picks the plain source.

## `[JsonMigratable]` on collections, dictionaries and unions

Applying the attribute to a type whose contract is not a JSON object now throws `JsonMigratableTargetKindNotSupportedException` (a `NotSupportedException`) when the converter is created, naming the type and pointing at the fix. 1.x failed later, inside System.Text.Json, with a generic "invalid operation for kind" error. Nothing that worked in 1.x is affected; the failure moved earlier and got a message.

## .NET 11

The package multi-targets `net10.0` and `net11.0`. On .NET 11, `AddJsonMigrationSupport()` also registers `JsonMigratableUnionTypeClassifier`, so a C# `union` whose cases are `[JsonMigratable]` types is classified by migration discriminator. Source-generated contexts must name the classifier on the union (`[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]`); see [polymorphism.md](polymorphism.md). The `[JsonPolymorphic]` limitation is unchanged.

## Checklist

1. Move `AddJsonMigrationSupport()` after any `TypeInfoResolver` assignment, or switch to `TypeInfoResolverChain.Add`.
2. If a custom resolver serves `[JsonMigratable]` types, insert it at index 0 after `AddJsonMigrationSupport()`.
3. Run your own round-trip tests against 1.x-written fixtures; the library's contract for them is unchanged.
