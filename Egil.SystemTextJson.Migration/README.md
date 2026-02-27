# Egil.SystemTextJson.Migration

`Egil.SystemTextJson.Migration` adds version-tolerant migration support on top of `System.Text.Json`.

## Features

- `[JsonMigratable]` type discriminator support.
- Explicit registration of `IMigrate<TSource, TTarget>` migrators.
- Optional assembly-scoped migrator scanning.
- Support for source-generated `JsonSerializerContext` metadata.
- Optional migration tracking through `IJsonMigrationTracked`.

## Quick start

```csharp
using System.Text.Json;
using Egil.SystemTextJson.Migration;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigrator<MyMigrator>();
});
```

Migration registration is explicit by default to keep startup behavior predictable and AOT-friendly.

## Mutation testing

Install local tools (or restore if already installed):

```bash
dotnet tool restore
```

Run mutation testing with Stryker.NET:

```bash
dotnet stryker --config-file stryker-config.json -t mtp
```

Reports are written under `StrykerOutput/`.
