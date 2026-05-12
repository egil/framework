# Polymorphism (`[JsonPolymorphic]`) Compatibility

This page documents a current limitation: **`[JsonMigratable]` cannot be combined with `[JsonPolymorphic]` / `[JsonDerivedType]` on the same type hierarchy**. It explains why, what happens if you try, and the recommended workarounds.

## Summary

| Scenario | Supported? |
|----------|------------|
| `[JsonPolymorphic]` on a base type with `[JsonDerivedType]` entries (no `[JsonMigratable]` anywhere in the hierarchy) | ✅ Works — uses System.Text.Json's built-in polymorphism |
| `[JsonMigratable]` on a non-polymorphic type | ✅ Works — this is the library's primary scenario |
| `[JsonMigratable]` on a `[JsonPolymorphic]` base type | ❌ Throws `NotSupportedException` at type-info configuration time |
| `[JsonPolymorphic]` on a base, `[JsonMigratable]` on a derived `[JsonDerivedType]` entry | ❌ Throws `NotSupportedException` at deserialization time |

## Why it doesn't work

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

Either of these exceptions:

```
System.NotSupportedException: The converter for derived type 'YourType'
does not support metadata writes or reads.
```

The first form fires at type-info configuration time when `[JsonMigratable]` is on the polymorphic base. The second fires at serialization/deserialization time when `[JsonMigratable]` is on a derived `[JsonDerivedType]` entry.

## Recommended workarounds

### Option 1 — Use `[JsonMigratable]` discriminator-based dispatch instead of `[JsonPolymorphic]`

`[JsonMigratable]` already provides discriminator-based dispatch — it just calls the property `$type` (or whatever you configure via `TypeDiscriminatorPropertyName`) and the value `TypeDiscriminator`. If your only reason to use `[JsonPolymorphic]` was to dispatch to subtypes by a discriminator, model each subtype as a `[JsonMigratable]` type and migrate between them as needed:

```csharp
[JsonMigratable(TypeDiscriminator = "dog")]
public class Dog { /* ... */ }

[JsonMigratable(TypeDiscriminator = "cat")]
public class Cat { /* ... */ }
```

Deserialize as the concrete type, or as a base interface, depending on your call site needs. See [type-discriminators.md](type-discriminators.md) for customization options.

### Option 2 — Keep `[JsonPolymorphic]` and migrate at the leaf types only

If you need full STJ polymorphism (for example, you rely on `JsonUnknownDerivedTypeHandling`, source-generator emission, or interop with another tool), keep `[JsonPolymorphic]` on the base type and **do not** put `[JsonMigratable]` anywhere in the hierarchy. Handle version migration outside the polymorphic layer — for example, deserialize the polymorphic payload, then transform old derived types to current ones in your application code.

### Option 3 — Wrap migratable types behind a non-polymorphic boundary

If a class needs to participate in a polymorphic hierarchy and also evolve over time, expose a stable wrapper type to the polymorphic layer and put `[JsonMigratable]` on the wrapper's payload property type instead of the wrapper itself. The polymorphic layer sees only the stable wrapper; the migration layer handles the inner payload.

## Future support

The .NET 11 timeframe is introducing `JsonTypeClassifier` ([dotnet/runtime#125449](https://github.com/dotnet/runtime/issues/125449), [#127299](https://github.com/dotnet/runtime/issues/127299)) — a new abstraction that lets custom code drive polymorphic type dispatch. If that ships and `CanHaveMetadata` becomes overridable ([dotnet/runtime#118900](https://github.com/dotnet/runtime/issues/118900)), it should become possible to combine `[JsonMigratable]` with STJ-level polymorphism. This is being tracked; until then, use one of the workarounds above.
