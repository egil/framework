# Error Handling & Diagnostics

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
