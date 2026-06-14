# Proposal: Add payload migration hooks to the Orleans serializer

## Summary

Add first-class serializer support for versioned payload migration in Orleans so that DTOs sent over Orleans RPC and streams by one silo version can be read as the current DTO type by another silo version without grain code receiving obsolete shapes. The feature mirrors the proven model from `Egil.SystemTextJson.Migration`: a serialized discriminator identifies the source version, Orleans deserializes that source version, invokes registered migration logic, and returns the requested target type to the grain method, stream observer, or response handler.

Orleans already has a stable type-discriminator concept through `[Alias]`, field ids, and encoded type headers. What is missing is a deserialization-time interception point that can say: “the wire payload says `UserV1`, the caller expected `UserV3`, so deserialize `UserV1`, migrate it to `UserV3`, and return `UserV3`.”

## Motivation

The most valuable scenario is mixed-version Orleans clusters during blue/green or rolling deployments. Most Orleans applications use `System.Text.Json`, Newtonsoft.Json, or storage-provider-native formats for grain storage. Orleans' built-in serializer is therefore primarily in the hot path for silo-to-silo RPC, client-to-silo RPC, grain call responses, and Orleans streams. During a deployment, two silo versions can legitimately coexist and exchange messages while each version targets a different grain interface or DTO contract.

Orleans serialization is already version-tolerant for additive/removal changes inside a type via `[Id]`-annotated fields, but many real schema changes require semantic transformations:

- split or merge fields;
- convert primitive or collection state into an object;
- rename or re-interpret values where field ids cannot express intent;
- migrate RPC request/response DTOs across rolling upgrades where old silos/clients still emit old aliases;
- migrate stream event payloads when producers and consumers upgrade independently;
- keep grain methods typed to the current DTO while accepting old payloads from in-flight RPC calls, stream events, reminders, and optional Orleans-serializer-backed storage.

Without built-in hooks, users must keep obsolete DTOs in grain APIs, run perfectly synchronized deployments, write custom codecs per type, add translation layers around every caller/receiver, or avoid changing DTO shapes until all in-flight messages drain. Those approaches make blue/green deployments harder and make it difficult to guarantee that grain code only sees the version it was compiled against.


## Primary scenario: blue/green RPC and stream compatibility

A representative deployment looks like this:

1. Version A silos expose `IOrdersV1`/`OrderSubmittedV1` contracts and send `OrderCommandV1` payloads.
2. Version B silos expose `IOrdersV2`/`OrderSubmittedV2` contracts and send `OrderCommandV2` payloads.
3. During blue/green rollout, both versions are active in the same cluster and may call each other or publish/consume from the same Orleans stream namespace.
4. The receiving silo should deserialize the actual wire DTO, run a declared migration, and deliver the DTO expected by the local grain method, stream observer, or response continuation.

The proposal is intentionally about Orleans serializer payload compatibility, not a complete interface-version router. If a request can already be routed to a compatible method identity via existing Orleans grain interface/method versioning practices, aliases, or application-level forwarding, the serializer should be able to adapt the argument, response, and stream item payloads at the boundary. A later extension could add method-level migration metadata, but the MVP should focus on DTO migration because that is the smallest reusable primitive across RPC and streams.

## Prior art: Egil.SystemTextJson.Migration

The JSON migration library demonstrates the desirable developer model:

```csharp
[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IMigrateFrom<UserV1, UserV2>
{
    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var parts = source.Name.Split(' ', 2);
        result = new UserV2(parts[0], parts.ElementAtOrDefault(1) ?? string.Empty, source.Age);
        return true;
    }
}
```

Key properties to preserve for Orleans:

- normal reads of the current type stay on the fast path;
- migration is selected by a stable discriminator/alias, not by CLR type name;
- both target-owned static migrations and separately registered migrators are useful;
- migration can be multi-source into one target;
- migration should be recursive for nested fields because field codecs already call back into the serializer;
- failure behavior should be explicit and observable.

## Current Orleans serializer observations

This proposal is based on the current `dotnet/orleans` main branch serializer implementation and public docs:

- `AliasAttribute` is the existing stable name mechanism for types and methods. Type aliases must be globally unique, which makes aliases suitable as stable payload discriminators.
- Orleans encodes the actual type in field headers when it differs from the expected type. `Writer.WriteFieldHeader` writes `SchemaType.Expected` for matching types and otherwise writes a well-known, referenced, or encoded actual type.
- `FieldHeaderCodec.ReadFieldHeader` reads that schema type back into `Field.FieldType`.
- Generated/reference type serializers such as `ConcreteTypeSerializer<TField,TBaseCodec>` compare `field.FieldType` with the expected codec type. If the incoming type is different, they delegate to `DeserializeUnexpectedType<TInput,TField>`.
- `DeserializeUnexpectedType` currently gets a codec for `field.FieldType`, reads that value, and casts it to `TField`. That cast is exactly the narrow point where old-version payloads fail if the source type is not assignable to the requested target type.
- `CodecProvider` already centralizes codec lookup and supports generalized/specializable codecs, which makes it a good place to introduce migration-aware wrappers without requiring every generated codec to change.
- `IBaseCodec<T>` and `IFieldCodec<T>` separate object-body serialization from field-level type dispatch. Migration from one concrete serialized object to another target naturally belongs at the field-level read boundary, after the source object has been decoded and before it is returned to the caller.

## Proposed public API shape

### Migration contracts

Add contracts to `Orleans.Serialization` or a dedicated namespace such as `Orleans.Serialization.Migrations`:

```csharp
public interface IMigrateFrom<TSource, TTarget>
    where TTarget : IMigrateFrom<TSource, TTarget>
{
    static abstract bool TryMigrateFrom(TSource source, out TTarget result);
}

public interface IMigrate<TSource, TTarget>
{
    bool TryMigrateFrom(TSource source, out TTarget result);
}
```

Static abstract target-owned migrations are allocation-free and source-generator friendly. External migrators support dependency injection and users who do not own the target type.

### Opt-in metadata

Prefer reusing `[Alias]` for the source discriminator rather than introducing a competing discriminator attribute:

```csharp
[GenerateSerializer, Alias("user-v1")]
public sealed record UserV1(...);

[GenerateSerializer, Alias("user-v2")]
public sealed record UserV2(...)
    : IMigrateFrom<UserV1, UserV2>;
```

Add optional target-side metadata only for policy and brownfield cases:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class MigrateToAttribute : Attribute
{
    public Type? UndiscriminatedSourceType { get; set; }
    public MigrationFailureBehavior FailureBehavior { get; set; } = MigrationFailureBehavior.Throw;
}
```

The name is intentionally illustrative. The actual attribute could be `[GenerateSerializerMigration]`, `[MigrationTarget]`, or options-only if the team prefers not to add another attribute.

### Builder registration

Extend `ISerializerBuilder`:

```csharp
public static ISerializerBuilder AddSerializerMigrations(
    this ISerializerBuilder builder,
    Action<SerializerMigrationOptions>? configure = null);

public sealed class SerializerMigrationOptions
{
    public SerializerMigrationOptions RegisterMigrator<TMigrator>();
    public SerializerMigrationOptions RegisterMigratorsFromAssembly(Assembly assembly);
    public MigrationFailureBehavior DefaultFailureBehavior { get; set; }
}
```

If Orleans prefers avoiding `ISerializerBuilder` extension state, the same registrations can be expressed as `IServiceCollection` registrations plus `TypeManifestOptions`/codegen metadata.

## Proposed internal architecture

### 1. Build a migration registry at startup

Create a singleton `SerializerMigrationRegistry` from:

- generated metadata for target-owned `IMigrateFrom<TSource,TTarget>` implementations;
- explicitly registered `IMigrate<TSource,TTarget>` services;
- optional assembly scanning where acceptable;
- aliases from `TypeManifestOptions.WellKnownTypeAliases`/`[Alias]` and type information from the manifest.

The registry should answer:

```csharp
bool TryGetMigration(Type sourceType, Type targetType, out ISerializerMigration migration);
bool HasMigrationsForTarget(Type targetType);
```

The resolved migration should expose an untyped fast path:

```csharp
public interface ISerializerMigration
{
    Type SourceType { get; }
    Type TargetType { get; }
    bool TryMigrate(object? source, out object? result);
}
```

### 2. Intercept unexpected-type deserialization

Change the unexpected-type helper from “deserialize source and cast” to “deserialize source, migrate if necessary, then cast”. Conceptually:

```csharp
public static TField DeserializeUnexpectedType<TInput, TField>(
    this ref Reader<TInput> reader,
    scoped ref Field field)
    where TField : class
{
    var sourceType = field.FieldType;
    var specificSerializer = reader.Session.CodecProvider.GetCodec(sourceType);
    var source = specificSerializer.ReadValue(ref reader, field);

    if (source is TField assignable)
    {
        return assignable;
    }

    if (reader.Session.CodecProvider.TryMigrate(sourceType, typeof(TField), source, out var migrated))
    {
        return (TField)migrated!;
    }

    return (TField)source;
}
```

This is the minimum viable hook for grain messages and nested object graphs because all field codecs funnel unexpected concrete object types through this path.

### 3. Add an optional current-type fast wrapper

The helper above handles discriminated old aliases (`field.FieldType != expected`). It does not handle brownfield payloads where the field header says “expected type” but the bytes actually represent an older shape because there was no old alias at write time.

For that case, optionally wrap codecs for targets with configured migrations:

- if the field header is `Expected`, delegate directly to the current codec by default;
- if `UndiscriminatedSourceType` is configured for the target, deserialize the object body as the configured source type and migrate;
- keep this off unless configured because it necessarily changes how matching expected-type payloads are interpreted.

This mirrors the JSON library’s one-source-per-target brownfield model.

### 4. Reference tracking and identity

The main subtlety is Orleans reference tracking:

- Current `ConcreteTypeSerializer` creates the target instance and records it before populating fields so cycles and repeated references can be resolved.
- A migration flow deserializes a complete source object and only then creates the target object. That is fine for acyclic DTOs, but not sufficient for cycles where other fields may reference the target before migration completes.

Recommended MVP: support migration for non-cyclic DTO/message/state payloads and reject or document cycles. Longer-term, add a placeholder target reference before source deserialization only when the migration contract can populate an existing target instance:

```csharp
public interface IPopulateMigration<TSource, TTarget>
{
    bool TryPopulateFrom(TSource source, TTarget target);
}
```

This mirrors Orleans’ existing surrogate `IPopulator` pattern.

### 5. Code generation integration

The source generator should:

- discover `IMigrateFrom<TSource,TTarget>` on generated serializer target types;
- emit metadata registrations so AOT/trimming does not rely on reflection scanning;
- validate that source and target types are serializable or have codecs;
- detect duplicate migration registrations and report diagnostics;
- optionally warn if a migration source lacks `[Alias]`, since stable aliases are required for long-lived payloads.

External migrators can be registered explicitly for AOT scenarios.

## Compatibility and rolling upgrades

The feature should not change the wire format for existing types. It should consume the existing field-header actual type information and `[Alias]` type names. That means:

- old silos can keep writing `UserV1` payloads;
- upgraded silos can receive fields expected as `UserV2` and migrate them;
- new silos write `UserV2` normally;
- users can keep old source types in a compatibility assembly until no stored/in-flight payload needs them.

For rolling upgrades, migrations should be deterministic, side-effect-free, and ideally idempotent. Documentation should recommend retaining migrations for at least one full data-retention window.

## Failure behavior and observability

Add configurable behavior:

```csharp
public enum MigrationFailureBehavior
{
    Throw,
    ReturnSourceWhenAssignable,
    ReturnNullForReferenceTypes
}
```

Default should be `Throw` with a serializer exception that includes source type/alias, target type/alias, and migrator type.

Emit metrics/logs for:

- migration attempted;
- migration succeeded;
- migration failed;
- missing migrator for source/target;
- duplicate migrator registrations.

## Tests to add in Orleans

1. Deserializing a field expected as `UserV2` from a wire payload with actual alias `user-v1` returns a `UserV2` after static migration.
2. The same scenario works for externally registered DI migrators.
3. Nested migratable fields migrate recursively.
4. No migration is attempted when actual type equals expected type.
5. Unknown source alias still follows existing unknown-type behavior.
6. Duplicate migration paths fail at startup or codegen with a clear diagnostic.
7. Failed `TryMigrateFrom` honors configured failure behavior.
8. Rolling-upgrade style test: serialize with old type metadata, deserialize with new target metadata.
9. AOT/source-generated registration test with no reflection scanning.
10. Reference-cycle behavior test documenting MVP rejection or verifying future populate-in-place support.

## Open questions

- Should migration live in `Orleans.Serialization` directly or in an optional package such as `Microsoft.Orleans.Serialization.Migrations`?
- Should Orleans expose target-owned static migration contracts, external DI migrators, or only one of them initially?
- Should the source generator require `[Alias]` on every migration source?
- How much brownfield “expected header but old body” support belongs in the core serializer versus storage-provider-specific migration tooling?
- Should migrations be allowed for value types, or should the first version focus on reference DTOs and grain state classes?
- What is the desired behavior for cyclic graphs and object identity during migration?

## Suggested MVP

1. Add migration contracts and explicit registration APIs.
2. Add a migration registry.
3. Update `DeserializeUnexpectedType` to migrate non-assignable source values to the requested target type before casting.
4. Add source-generator metadata for static `IMigrateFrom<TSource,TTarget>`.
5. Document that MVP migrations support non-cyclic DTOs and require aliases for stored/wire compatibility.

This would provide the highest-value scenario with the smallest serializer change: old aliased RPC arguments, RPC responses, and stream payloads can be accepted by current grain and observer code while preserving Orleans’ existing wire format and normal fast path.
