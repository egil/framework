# Upgrading to 2.0

2.0 changes no public API: every type, method and attribute from 1.x is still there with the same signature. The major version signals changes in behavior. This page lists them, with the check to run against your code and the fix when it applies.

## Stored data

JSON written by 1.x is read by 2.0 exactly as 1.x read it. The discriminator property, its default value (the type's full name), its first position in the object, the legacy-payload rule for objects without a discriminator, and `UndiscriminatedSourceType` are unchanged. The 1.7 release's own test suite, run unmodified against 2.0, passes every payload test; the one test that fails covers an options-setup change described below, not a payload.

What changed on the read side is additive: payloads that 1.x rejected can now migrate (see [non-object payloads](#non-object-payload-matching)), and no payload that 1.x migrated is routed differently.

## Migration support lives in the resolver chain

`AddJsonMigrationSupport()` used to append a converter factory to `options.Converters`. It now inserts a resolver at the front of `options.TypeInfoResolverChain` and leaves `options.Converters` alone, so types outside migration keep System.Text.Json's source-generated fast-path serializers (see [Keeping the generated fast path](aot-source-gen.md#keeping-the-generated-fast-path)). Two setups behave differently.

**A custom `IJsonTypeInfoResolver` that overrides a `[JsonMigratable]` type.** In 1.x a resolver that returned its own contract for a `[JsonMigratable]` type won silently, because it never consulted `options.Converters`. In 2.0 migration owns the type unless the resolver sits ahead of it in the chain; otherwise the non-object contract is refused with `NotSupportedException` the first time the type is used.

- Check: do you set `TypeInfoResolver` or add to `TypeInfoResolverChain` a resolver that returns contracts for `[JsonMigratable]` types?
- Fix: insert it ahead of migration, after `AddJsonMigrationSupport()`:

```csharp
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Insert(0, new MyResolver());
```

**Replacing the chain after registration.** Assigning `options.TypeInfoResolver = ...` or calling `options.TypeInfoResolverChain.Clear()` after `AddJsonMigrationSupport()` removes migration support. There is no error: `[JsonMigratable]` types then serialize without `$type` and old payloads deserialize as the current type. In 1.x the converter list carried migration through such changes.

- Check: search for `TypeInfoResolver =` and `TypeInfoResolverChain.Clear` after the call to `AddJsonMigrationSupport()`.
- Fix: call `AddJsonMigrationSupport()` last, or use `TypeInfoResolverChain.Add(...)` to add contexts, which is the order the README shows.

Unchanged: converters in `options.Converters` keep their precedence. One registered before `AddJsonMigrationSupport()` still wins for a `[JsonMigratable]` type; one registered after still does not. Calling `AddJsonMigrationSupport()` twice keeps the first registration, as before. Options without any resolver keep reflection-based serialization.

## Non-object payload matching

1.x matched a scalar payload to a source type by `TypeCode` (`string`, `bool`, the primitive numerics and enums) and a JSON array to any enumerable source. 2.0 matches by the JSON token family the source's converter reads, which widens what can migrate:

- numbers reach `Half`, `Int128`, `UInt128` and, on .NET 11, `BFloat16` and `Decimal32/64/128`;
- strings reach `char`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `Uri`, `Version`, `byte[]`, `Memory<byte>` and `ReadOnlyMemory<byte>`;
- a quoted number reaches a numeric source under `JsonNumberHandling.AllowReadingFromString` (on by default with `JsonSerializerDefaults.Web`) when no string-shaped source exists;
- several collection sources are told apart by the first element's discriminator or contract kind instead of throwing.

Every one of these was a `JsonException` in 1.x, so existing data is unaffected. One rule is stricter: a source read through a converter override (`[JsonConverter]` on the type, an entry in `options.Converters` such as `JsonStringEnumConverter`, or a converter attached by the resolver) is no longer shape-matched, because the override may read a different token family. The exception that keeps 1.x data readable is an enum source read through `JsonStringEnumConverter`: a JSON number still reaches it when no other source matched, since that converter accepts integers by default.

- Check: a target with `IMigrateFrom<TSource, ...>` where `TSource` is not an enum and has a converter override, together with stored non-object payloads for it.
- Fix: such a source is reachable only through a discriminator, so migrate it from an object payload, or register the migration from the underlying primitive type instead.

## `[JsonMigratable]` on collections, dictionaries and unions

Applying the attribute to a type whose contract is not a JSON object now throws `JsonMigratableTargetKindNotSupportedException` (a `NotSupportedException`) when the converter is created, naming the type and pointing at the fix. 1.x failed later, inside System.Text.Json, with a generic "invalid operation for kind" error. Nothing that worked in 1.x is affected; the failure moved earlier and got a message.

## .NET 11

The package multi-targets `net10.0` and `net11.0`. On .NET 11, `AddJsonMigrationSupport()` also registers `JsonMigratableUnionTypeClassifier`, so a C# `union` whose cases are `[JsonMigratable]` types is classified by migration discriminator. Source-generated contexts must name the classifier on the union (`[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]`); see [polymorphism.md](polymorphism.md). The `[JsonPolymorphic]` limitation is unchanged.

## Checklist

1. Move `AddJsonMigrationSupport()` after any `TypeInfoResolver` assignment, or switch to `TypeInfoResolverChain.Add`.
2. If a custom resolver serves `[JsonMigratable]` types, insert it at index 0 after `AddJsonMigrationSupport()`.
3. If a non-enum source type has a converter override and stored non-object payloads, migrate it through an object payload or from the primitive type.
4. Run your own round-trip tests against 1.x-written fixtures; the library's contract for them is unchanged.
