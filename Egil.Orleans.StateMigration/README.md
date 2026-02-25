## Egil.Orleans.StateMigration (proposal)

Goal: make it easier to upgrade JSON state (for example in blob storage) from older .NET types to newer/different types, without placing migration logic inside each grain.

## Core idea

Wrap state in `Storage<TStateType>`.

The stored JSON contains type metadata (`$type`) so the converter can decide whether:
- state is already the current type, or
- migration is needed using one of the supported migration interfaces.

Migration interfaces:

`IMigrateFrom<TSource, TTarget>` for type-owned static migrations on `TTarget`:

`static abstract TTarget From(TSource source)`

`IMigrate<TSource, TTarget>` for external migrator classes:

`TTarget Migrate(TSource source)`

Resolution order for a `(TSource, TTarget)` pair:
1. Prefer `IMigrateFrom<TSource, TTarget>` on `TTarget`.
2. Fallback to a registered external `IMigrate<TSource, TTarget>`.
3. Fail with a clear exception if no migrator exists.

External migrator registration/lifetime:
- Register as singleton.
- Expect stateless/thread-safe implementations.
- Fail startup if multiple external migrators are registered for the same `(TSource, TTarget)` pair.

If migration happens during deserialization, set `Storage<TStateType>.MigratedDuringDeserialization = true` so callers know they should write the updated format back.
The same flag is also set when payload layout differs from configured output layout (for example legacy flattened payload read while enveloped output is configured).

## Type identity contract

When serializing `Storage<T>`:
- By default, write `$type` and `$value` (both configurable via `AddStateMigrationSupport(options, typePropertyName, valuePropertyName)`).
- By default, write an envelope payload shape: `{"$type":"...","$value": ...state...}`.
- Optional compatibility mode can write the legacy flattened shape: `{"$type":"...", ...state object properties...}`.
- If `T` has Orleans `[Alias]`, use the alias value.
- Otherwise use the full CLR type name (compatibility fallback, same spirit as Orleans serialization behavior).

Performance guidance:
- `Enveloped` layout is the default and optimized hot path. When no migration is needed, this path targets near-plain STJ cost.
- `Flattened` layout is compatibility-focused and may allocate more during serialization because state properties must be merged into the root object.

Guidance:
- Aliases should be treated as immutable once data is persisted.
- Validate at startup:
  - duplicate aliases
  - unresolved `$type` mappings
- Emit logs/metrics when CLR-name fallback is used so systems can migrate toward aliases.

## `$type` position and deserialization flow

`$type` being the first property is a storage format contract for the versioned format. This is intentional to keep deserialization low-allocation.

Proposed flow:
1. Copy `Utf8JsonReader` and inspect only the first property.
2. If first property is `$type` with a non-empty string:
   - If payload is enveloped (`$type` + `$value`), deserialize `$value`.
   - Legacy envelopes with `value` are still accepted for backward compatibility and can be rewritten.
   - If payload is flattened (legacy), deserialize full object as state/source type.
   - If value matches target `T`, fast-path deserialize current type.
   - If value is another known type, deserialize that type, resolve migration for `(sourceType, T)`, then set `MigratedDuringDeserialization = true`.
   - If value is null/empty/unknown, fail fast with a clear exception.
3. If first property is not `$type`, treat payload as legacy/unversioned:
   - deserialize as current `T`
   - set `MigratedDuringDeserialization = true` so next write emits versioned format

Note: external JSON tooling that reorders properties can violate this contract.

## Migration scope

Multi-hop migration is intentionally not supported.

Rationale:
- Avoid creating intermediate objects when migrating through many versions.
- Keep deserialization and migration predictable and allocation-aware.

Expectation:
- For each historical source type that may exist in storage, there is a direct migration to the latest type, either:
  - on the target type via `IMigrateFrom<TSource, TTarget>`
  - in an external singleton migrator via `IMigrate<TSource, TTarget>`

## Orleans callback integration

After **deserialization**, if target `T` implements Orleans `IOnDeserialized`, call `OnDeserialized`.

Reference:
https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/CodeGeneration/IOnDeserialized.cs

`DeserializationContext` can be retrieved from `IServiceProvider` via `OnDeserializedCallbacks`.

## API sketch

```csharp
public interface IMigrateFrom<in TSource, TTarget>
{
    static abstract TTarget From(TSource source);
}

public interface IMigrate<in TSource, out TTarget>
{
    TTarget Migrate(TSource source);
}

[JsonConverter(typeof(StorageJsonConverterFactory))]
public class Storage<TStateType>
{
    public required TStateType Value { get; set; }

    public bool MigratedDuringDeserialization { get; init; }
}

internal class StorageJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(Storage<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var stateType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(StorageJsonConverter<>).MakeGenericType(stateType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

internal class StorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
{
    public override Storage<TStateType>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotImplementedException();

    public override void Write(Utf8JsonWriter writer, Storage<TStateType> value, JsonSerializerOptions options)
        => throw new NotImplementedException();
}
```

## Orleans use case

Instead of `IPersistentState<T>`, inject `IPersistentState<Storage<T>>`.

This keeps migration logic out of grain classes and centralized in the serializer/converter layer.

## Usage examples

### Example 1: type-owned static migration (`IMigrateFrom<,>`)

```csharp
using Orleans;

[Alias("cart-state/v1")]
public sealed class CartStateV1
{
    public List<string> ItemIds { get; set; } = [];
}

[Alias("cart-state/v2")]
public sealed class CartStateV2 : IMigrateFrom<CartStateV1, CartStateV2>
{
    public required List<CartItem> Items { get; init; }

    public static CartStateV2 From(CartStateV1 source)
        => new()
        {
            Items = source.ItemIds.Select(id => new CartItem { ProductId = id, Quantity = 1 }).ToList()
        };
}

public sealed class CartItem
{
    public required string ProductId { get; init; }
    public int Quantity { get; init; }
}
```

Grain usage:

```csharp
public sealed class CartGrain([PersistentState("cart")] IPersistentState<Storage<CartStateV2>> state) : Grain
{
    public Task Update(CartStateV2 state)
    {
        this.state.State.Value = state;
        return this.state.WriteStateAsync();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (state.State.MigratedDuringDeserialization)
        {
            await state.WriteStateAsync();
        }
    }
}
```

### Example 2: external migrator (`IMigrate<,>`)

```csharp
[Alias("profile-state/v1")]
public sealed class ProfileStateV1
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

[Alias("profile-state/v2")]
public sealed class ProfileStateV2
{
    public required string DisplayName { get; init; }
}

public sealed class ProfileV1ToV2Migrator : IMigrate<ProfileStateV1, ProfileStateV2>
{
    public ProfileStateV2 Migrate(ProfileStateV1 source)
        => new()
        {
            DisplayName = $"{source.FirstName} {source.LastName}".Trim()
        };
}
```

DI registration:

```csharp
services.AddSingleton<IMigrate<ProfileStateV1, ProfileStateV2>, ProfileV1ToV2Migrator>();
```

External migrators are resolved through `IMigrationResolver` and are useful for application-managed migration flows where migration logic should not live on the target type.
`Storage<T>` converter deserialization uses target-owned static migrations (`IMigrateFrom<,>`) and sets `MigratedDuringDeserialization = true` when migration succeeds.

## Benchmarks

`perf/Egil.Orleans.StateMigration.PerfTests` contains BenchmarkDotNet scenarios for the "no migration needed" hot path.
The benchmark config uses BenchmarkDotNet's `InProcessNoEmit` toolchain so the suite runs reliably with this repo's strict analyzer settings.

The suite covers both:
- a minimal state object (`MinimalState`)
- a more realistic nested state object (`ComplexState`)

For each state profile, benchmarks compare:
- plain STJ (`JsonSerializer`) serialize/deserialize
- `Storage<T>` with state migration support (reflection options)
- `Storage<T>` with source-generated STJ context for state types only
- `Storage<T>` with source-generated STJ context that also includes closed `Storage<T>` metadata

The suite also includes direct payload layout comparison benchmarks for `Storage<T>`:
- `Enveloped` (default)
- `Flattened` (legacy non-enveloped shape)

Run all benchmarks:

```bash
dotnet run -c Release --project perf/Egil.Orleans.StateMigration.PerfTests/Egil.Orleans.StateMigration.PerfTests.csproj
```

Run just deserialization benchmarks:

```bash
dotnet run -c Release --project perf/Egil.Orleans.StateMigration.PerfTests/Egil.Orleans.StateMigration.PerfTests.csproj -- --filter "*Deserialize*"
```

Run only enveloped vs non-enveloped layout comparisons:

```bash
dotnet run -c Release --project perf/Egil.Orleans.StateMigration.PerfTests/Egil.Orleans.StateMigration.PerfTests.csproj -- --filter "*PayloadLayout*"
```
