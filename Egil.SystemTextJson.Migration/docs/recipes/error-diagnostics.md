# Error Handling & Diagnostics

## Analyzer warning: invalid migratable target contract (STJM0002)

`[JsonMigratable]` target types use `IMigrateFrom<TSource, TTarget>` for migrations declared on the target. `IMigrate<TSource, TTarget>` is reserved for a separate external migrator type. The analyzer reports STJM0002 on an `IMigrate` interface implemented directly by a migratable target:

```cs
[JsonMigratable]
public sealed class ProductV2 : IMigrate<ProductV1, ProductV2> // STJM0002
{
}
```

Move the external migration contract to another type, or use the target-owned contract instead:

```cs
[JsonMigratable]
public sealed class ProductV2 : IMigrateFrom<ProductV1, ProductV2>
{
}
```

For a directly declared contract, the warning is attached to that interface in the target's base list. When the contract is inherited, the warning is attached to the derived migratable target because no direct interface occurrence exists there.

## Analyzer warning: undiscriminated source without a migrator (STJM0003)

`UndiscriminatedSourceType` selects a source type for payloads that do not contain a discriminator. The selected source needs either a target-owned `IMigrateFrom<TSource, TTarget>` contract or a visible external `IMigrate<TSource, TTarget>` contract. STJM0003 warns when neither contract is available:

```cs
[JsonMigratable(UndiscriminatedSourceType = typeof(ProductV1))] // STJM0003
public sealed class ProductV2
{
}
```

Add a matching target-owned migration or an external migrator contract before configuring the undiscriminated source.
## STJM0005: `JsonMigratable` conflicts with System.Text.Json polymorphism

`[JsonMigratable]` cannot be used in a type hierarchy that also uses
`[JsonPolymorphic]` or `[JsonDerivedType]`. System.Text.Json requires polymorphic
converters to participate in its metadata protocol, which migration converters cannot
currently support. The analyzer reports the conflict before it fails at runtime.

Use a .NET 11 C# union with migratable case types, migrate outside the polymorphic
hierarchy, or introduce a stable polymorphic wrapper. See the
[polymorphism recipe](polymorphism.md) for the limitation and supported designs.
## Handling unknown discriminators

When a `$type` discriminator value doesn't match any registered source type or the target type itself, the library throws a `JsonException` with a clear message identifying the unrecognized discriminator:

<!-- snippet: error_unknown_discriminator -->
<a id='snippet-error_unknown_discriminator'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// "product-v99" is not registered as a source type for ItemV2
var json = """{"$type":"product-v99","name":"Widget"}""";

var ex = Assert.Throws<JsonException>(
    () => JsonSerializer.Deserialize<ItemV2>(json, options));
// ex.Message contains details about the unrecognized discriminator
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ErrorDiagnosticsSample.cs#L21-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-error_unknown_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This typically means a source type was removed from the codebase without migrating all stored data, or a discriminator value was changed. Check that all historical source types are still registered.

## Duplicate discriminator detection

If two different source types resolve to the same discriminator value for the same target type, the library throws `JsonMigrationDuplicateTypeDiscriminatorException` when the first deserialization is attempted:

<!-- snippet: error_duplicate_discriminator_types -->
<a id='snippet-error_duplicate_discriminator_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "dup-src")]
public record class DupSourceA(string Data);

[JsonMigratable(TypeDiscriminator = "dup-src")]
public record class DupSourceB(string Data);

[JsonMigratable]
public record class DupTarget(string Data);

public class DupMigratorA : IMigrate<DupSourceA, DupTarget>
{
    public bool TryMigrateFrom(DupSourceA source, out DupTarget result)
    {
        result = new DupTarget(source.Data);
        return true;
    }
}

public class DupMigratorB : IMigrate<DupSourceB, DupTarget>
{
    public bool TryMigrateFrom(DupSourceB source, out DupTarget result)
    {
        result = new DupTarget(source.Data);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ErrorDiagnosticsSample.cs#L84-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-error_duplicate_discriminator_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: error_duplicate_discriminator -->
<a id='snippet-error_duplicate_discriminator'></a>
```cs
// Two different source types resolve to the same discriminator value.
// This is detected when the first deserialization attempt is made.
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(static builder =>
    builder
        .RegisterMigrator<DupMigratorA>()
        .RegisterMigrator<DupMigratorB>());

var json = """{"$type":"dup-src","data":"test"}""";

var ex = Assert.Throws<JsonMigrationDuplicateTypeDiscriminatorException>(
    () => JsonSerializer.Deserialize<DupTarget>(json, options));
// ex.Discriminator == "dup-src"
// ex.TargetType == typeof(DupTarget)
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ErrorDiagnosticsSample.cs#L39-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-error_duplicate_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This is a configuration error. Each source type must have a unique discriminator value within the scope of a single target type. Use explicit `TypeDiscriminator` values on `[JsonMigratable]` to avoid collisions.

## `UnmappedMemberHandling.Disallow` compatibility

The library works correctly with `JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. The `$type` discriminator property is consumed by the migration pipeline and does not leak as an unmapped member on the target type:

<!-- snippet: error_unmapped_member_handling -->
<a id='snippet-error_unmapped_member_handling'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
};
options.AddJsonMigrationSupport();

// Even with strict unmapped-member checks, migration works correctly.
// The $type discriminator is consumed by the library and does not leak
// as an unmapped member on the target type.
var json = """{"$type":"item-v1","name":"Widget"}""";

var item = JsonSerializer.Deserialize<ItemV2>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ErrorDiagnosticsSample.cs#L63-L76' title='Snippet source file'>snippet source</a> | <a href='#snippet-error_unmapped_member_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This means you can use strict unmapped-member validation in your application without any special configuration for migratable types.

## Legacy payload types (STJM0010)

Apply `[JsonMigrationLegacyType]` to a class, struct, or enum kept only to read historical JSON. A **source** is the input to one migration edge; a **target** is that edge's output. An intermediate type in a chain can be both a target and a source, and can itself be marked legacy. A **current type** is the type application code uses after migration.

The marker has no runtime effect. It neither enables migration nor registers a migrator, and it is not inherited by derived types. Keep `[JsonMigratable]` and existing registration where they are needed.

STJM0010 warns when a marked type is named or inferred outside its own declaration or the method implementing its direct `IMigrateFrom<TSource, TTarget>` / `IMigrate<TSource, TTarget>` edge. This includes object creation, `var`, ordinary fields, properties, constructors and APIs on the replacement type, and unrelated helper methods. Tests follow the same rule. A migration from V2 to V3 does not allow unrelated V1 usage merely because V1 can migrate to V2.

Required setup remains allowed: the direct migration interface declaration, the first (`TSource`) type argument of `RegisterMigrator<TSource, TTarget, TMigrator>` on `JsonMigrationBuilder`, `[JsonSerializable(typeof(LegacyType))]`, and `[JsonMigratable(UndiscriminatedSourceType = typeof(LegacyType))]`. Exemptions use the actual STJM and System.Text.Json symbols; unrelated APIs with matching names do not qualify.

```csharp
[JsonMigrationLegacyType]
[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name);

[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string Name) : IMigrateFrom<UserV1, UserV2>
{
    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        result = new UserV2(source.Name);
        return true;
    }
}
```

Deserialize application payloads as the current type and keep legacy access within the direct migration implementation. When an intentional exception is necessary, use the standard C# diagnostic suppression mechanisms at the narrowest useful scope.

`JsonMigrationLegacyTypeAttribute.MigratedExternally` defaults to `false`. It indicates a migration supplied outside the current compilation and does not suppress STJM0010.

## Orphaned legacy payload types (STJM0011)

STJM0011 warns when a `[JsonMigrationLegacyType]` declaration has no visible `IMigrateFrom<TSource, TTarget>` or `IMigrate<TSource, TTarget>` contract that uses it as `TSource` in the same compilation. Any target type satisfies the rule because the marker records that the legacy type still has a migration source edge.

If the migrator lives in another assembly, set `MigratedExternally = true` on the marker. This suppresses STJM0011 only; STJM0010 still restricts ordinary use of the marked type. If there is no migration remaining, remove the marker and consider deleting the historical type.

## STJM0004: migratable targets must be JSON objects

`[JsonMigratable]` adds a discriminator property to the target's JSON contract. Collections, dictionaries, and .NET 11 unions cannot carry that property, so the runtime rejects them. STJM0004 warns at the target declaration before serialization or deserialization is attempted. It also recognizes inherited migration markers and asynchronous enumerable targets.

Use an ordinary class or struct as the target, with collection or dictionary values in its properties. For a .NET 11 union, mark its object case types with `[JsonMigratable]` and leave the union itself unmarked. A `GetEnumerator` method alone does not make an ordinary object target a collection; the analyzer checks the collection interfaces.

This warning covers statically identifiable collection and union shapes. The runtime still validates the resolved JSON contract, including contracts supplied by converters or custom resolvers.
