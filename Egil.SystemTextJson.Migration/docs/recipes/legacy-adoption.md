# Legacy & Incremental Adoption

## Adopting the library with existing stored JSON

Payloads written before migration support was added won't have a `$type` discriminator. The library handles this gracefully — any JSON without a recognized discriminator in the first property position is treated as a **legacy payload** and deserialized directly as the target type.

<!-- snippet: legacy_no_discriminator -->
<a id='snippet-legacy_no_discriminator'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Existing stored JSON — written before migration support was added.
// No $type property at all. The library treats it as the target type directly.
var json = """{"itemName":"Widget","quantity":5}""";

var order = JsonSerializer.Deserialize<OrderV2>(json, options);
// Works perfectly — existing data keeps working with zero changes.
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L39-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_no_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This means you can adopt the library incrementally. Existing stored JSON keeps working with zero data migration. New writes will include the `$type` discriminator, and over time, old payloads can be upgraded via the [read-migrate-write-back pattern](migration-tracking.md#read-migrate-write-back-pattern).

## Migrating discriminator-less object payloads from a source type

If the stored JSON represents an older object shape, but the new target type has a different shape, configure the target with `UndiscriminatedSourceType`. This tells the library which single source type to assume when an object payload has no recognized first-property discriminator:

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

> **Note:** This opt-in supports one source type per target. If a target can migrate from multiple object source types, choose the one that represents discriminator-less stored payloads. Payloads with a recognized discriminator still use discriminator-based matching.

## Discriminator not in first position

The library only inspects the **first JSON property** for the type discriminator. If `$type` appears anywhere else in the payload, it is ignored and the payload follows the same path as any other object without a recognized discriminator: target deserialization by default, or the configured `UndiscriminatedSourceType` migrator when that opt-in is set.

<!-- snippet: legacy_discriminator_not_first -->
<a id='snippet-legacy_discriminator_not_first'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// The $type property exists but is NOT the first property.
// The library only checks the first property for the discriminator,
// so this is treated as a legacy payload and deserialized as-is.
var json = """{"itemName":"Widget","quantity":5,"$type":"order-v2"}""";

var order = JsonSerializer.Deserialize<OrderV2>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L59-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_discriminator_not_first' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This design is intentional — checking only the first property allows for O(1) discriminator detection without buffering the entire JSON payload. When serializing, the library always writes `$type` as the first property.
