# Real-World Integration Patterns

## Database / cache migration on read

When reading JSON from a database or cache, the library migrates old payloads transparently. Combine with `IJsonMigrationTracked` to optionally write back the updated format:

<!-- snippet: read_migrate_write_back_types -->
<a id='snippet-read_migrate_write_back_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "document-v2")]
public record class DocumentV2 : IJsonMigrationTracked,
    IMigrateFrom<DocumentV1, DocumentV2>
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;

    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(DocumentV1 source, out DocumentV2 result)
    {
        result = new DocumentV2
        {
            Title = source.Title,
            Body = source.Body,
            Slug = source.Title.ToLowerInvariant().Replace(' ', '-'),
        };
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ReadMigrateWriteBackSample.cs#L6-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-read_migrate_write_back_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

```csharp
// Pseudocode for database read with migration
var json = await database.GetJsonAsync("user:123");
var user = JsonSerializer.Deserialize<DocumentV2>(json, options);

if (user is { MigratedDuringDeserialization: true })
{
    // Write back the migrated version to eliminate future migration cost
    var updatedJson = JsonSerializer.Serialize(user, options);
    await database.SetJsonAsync("user:123", updatedJson);
}
```

> **Note:** The write-back is optional but recommended for frequently-read data. It converts the O(n) migration cost into a one-time operation. See [Read-migrate-write-back pattern](migration-tracking.md#read-migrate-write-back-pattern) for a complete working example.

## Message queue / event store migration

Events from a queue or event store are deserialized with migration support. Old event versions are transparently migrated to the current schema:

```csharp
// Configure options once at startup
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// In your message handler / event processor:
async Task HandleMessageAsync(string jsonPayload)
{
    var evt = JsonSerializer.Deserialize<MyEventV3>(jsonPayload, options);
    // evt is always MyEventV3, regardless of whether the original payload
    // was V1, V2, or V3.
    await ProcessEventAsync(evt);
}
```

> **Note:** For event stores where rewriting history is undesirable, migration happens only at read time — the stored events remain unchanged. New events are written with the current schema and discriminator.

## ASP.NET Core integration

Configure migration support in `Program.cs` with DI wiring for migrators and OpenTelemetry monitoring:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register migrator dependencies
builder.Services.AddTransient<MyMigrator>();

// Configure JSON options with migration support
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.AddJsonMigrationSupport(
        builder.Services.BuildServiceProvider(),
        migrationBuilder =>
        {
            migrationBuilder
                .RegisterMigratorsFromAssembly(typeof(Program).Assembly)
                .UseServiceProvider(builder.Services.BuildServiceProvider());
        });
});

// Subscribe to migration telemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(JsonMigrationTelemetry.MeterName);
        // Add your exporter (Prometheus, OTLP, etc.)
    });
```

> **Note:** For minimal APIs, use `ConfigureHttpJsonOptions`. For MVC controllers, configure `JsonOptions` via `AddControllers().AddJsonOptions(...)`. The `IServiceProvider` passed to `AddJsonMigrationSupport` enables DI resolution for migrators.

## Orleans grain state migration

A grain uses `IPersistentState<T>` and `T`'s schema changes between deployments. Configure Orleans' JSON serializer with migration support so that when the grain activates and reads its persisted state, old payloads are migrated transparently:

```csharp
// In your Orleans silo configuration (Program.cs):
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddMemoryGrainStorageAsDefault(); // or your storage provider

    // Configure the storage provider's JSON serializer
    siloBuilder.Services.AddOptions<OrleansJsonSerializerOptions>("Default")
        .Configure(options =>
        {
            options.JsonSerializerOptions.AddJsonMigrationSupport(migrationBuilder =>
            {
                migrationBuilder.RegisterMigratorsFromAssembly(typeof(Program).Assembly);
            });
        });
});
```

```csharp
// Grain state types evolve over time:
[JsonMigratable(TypeDiscriminator = "cart-v1")]
public record class ShoppingCartV1(List<string> Items);

[JsonMigratable(TypeDiscriminator = "cart-v2")]
public record class ShoppingCartV2(List<CartItem> Items)
    : IMigrateFrom<ShoppingCartV1, ShoppingCartV2>
{
    public static bool TryMigrateFrom(ShoppingCartV1 source, out ShoppingCartV2 result)
    {
        result = new ShoppingCartV2(
            source.Items.Select(name => new CartItem(name, 1)).ToList());
        return true;
    }
}

public record class CartItem(string Name, int Quantity);

// The grain reads state normally — migration happens during deserialization:
public class ShoppingCartGrain : Grain
{
    private readonly IPersistentState<ShoppingCartV2> cart;

    public ShoppingCartGrain(
        [PersistentState("cart")] IPersistentState<ShoppingCartV2> cart)
    {
        this.cart = cart;
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await cart.ReadStateAsync(); // Old V1 data is migrated to V2 here
    }
}
```

> **Note:** No manual migration step or versioned state classes are needed. When the grain activates, the storage provider deserializes the JSON using the configured options, and the library handles the migration. Subscribe to `JsonMigrationTelemetry.MeterName` to monitor migration volume across your cluster.

### Using Orleans `[Alias]` as the type discriminator

If your grain state types already use Orleans' `[Alias]` attribute for serialization identity, you can derive the migration type discriminator directly from it using `GetTypeDiscriminatorFrom<AliasAttribute>`. This avoids duplicating discriminator strings and, when all migratable types use `[Alias]`, leverages the Orleans analyzer's compile-time enforcement that alias values are globally unique:

```csharp
// In your Orleans silo configuration (Program.cs):
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddMemoryGrainStorageAsDefault(); // or your storage provider

    // Configure the storage provider's JSON serializer
    siloBuilder.Services.AddOptions<OrleansJsonSerializerOptions>("Default")
        .Configure(options =>
        {
            options.JsonSerializerOptions.AddJsonMigrationSupport(migrationBuilder =>
            {
                migrationBuilder
                    .RegisterMigratorsFromAssembly(typeof(Program).Assembly)
                    .GetTypeDiscriminatorFrom<AliasAttribute>(attr => attr.Alias);
            });
        });
});
```

```csharp
// Grain state types use [Alias] for both Orleans type identity and migration discriminators:
[Alias("cart-v1")]
[JsonMigratable]
public record class ShoppingCartV1(List<string> Items);

[Alias("cart-v2")]
[JsonMigratable]
public record class ShoppingCartV2(List<CartItem> Items)
    : IMigrateFrom<ShoppingCartV1, ShoppingCartV2>
{
    public static bool TryMigrateFrom(ShoppingCartV1 source, out ShoppingCartV2 result)
    {
        result = new ShoppingCartV2(
            source.Items.Select(name => new CartItem(name, 1)).ToList());
        return true;
    }
}

public record class CartItem(string Name, int Quantity);
```

> **Tip:** When all migratable types derive their discriminator from `[Alias]`, the Orleans analyzer validates uniqueness at compile time, helping prevent runtime `JsonMigrationDuplicateTypeDiscriminatorException` errors. Note that types without `[Alias]` fall back to `JsonMigratableAttribute.TypeDiscriminator` and are not covered by the analyzer check. See [Deriving discriminators from an existing attribute](type-discriminators.md#deriving-discriminators-from-an-existing-attribute) for details on `GetTypeDiscriminatorFrom`.
