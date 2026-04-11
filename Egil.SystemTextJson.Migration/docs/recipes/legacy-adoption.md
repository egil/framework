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
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L22-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_no_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This means you can adopt the library incrementally. Existing stored JSON keeps working with zero data migration. New writes will include the `$type` discriminator, and over time, old payloads can be upgraded via the [read-migrate-write-back pattern](migration-tracking.md#read-migrate-write-back-pattern).

## Discriminator not in first position

The library only inspects the **first JSON property** for the type discriminator. If `$type` appears anywhere else in the payload, it is ignored and the payload is treated as a legacy payload.

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
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/LegacyPayloadSample.cs#L42-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-legacy_discriminator_not_first' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This design is intentional — checking only the first property allows for O(1) discriminator detection without buffering the entire JSON payload. When serializing, the library always writes `$type` as the first property.
