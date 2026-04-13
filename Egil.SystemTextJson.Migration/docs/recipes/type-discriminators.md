# Type Discriminators

## Customizing discriminator values

By default, the type discriminator is the type's full name. Use `[JsonMigratable(TypeDiscriminator = "...")]` to set short, meaningful discriminator values:

<!-- snippet: custom_discriminator_attribute -->
<a id='snippet-custom_discriminator_attribute'></a>
```cs
// Per-type via the attribute:
[JsonMigratable(
    TypeDiscriminator = "user-v2",
    TypeDiscriminatorPropertyName = "version")]
public record UserV2(string FirstName, string LastName, int Age);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-custom_discriminator_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: custom_discriminator_builder -->
<a id='snippet-custom_discriminator_builder'></a>
```cs
// Or set a global default property name via the builder:
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.SetTypeDiscriminatorPropertyName("_schema");
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L28-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-custom_discriminator_builder' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Keep discriminators short and stable — they are persisted in your JSON payloads. Changing a discriminator value after data has been stored will break deserialization of old payloads.

## Customizing the discriminator property name

The default discriminator property name is `$type`. Change it globally using `SetTypeDiscriminatorPropertyName()`:

<!-- snippet: discriminator_property_name -->
<a id='snippet-discriminator_property_name'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(static builder =>
    builder.SetTypeDiscriminatorPropertyName("_schema"));

// Serialization uses the custom property name
var setting = new SettingV2("theme", "dark", "ui");
var json = JsonSerializer.Serialize(setting, options);
// json contains "_schema":"setting-v2" instead of "$type":"setting-v2"

// Deserialization reads the custom property name
var legacyJson = """{"_schema":"setting-v1","key":"theme","value":"dark"}""";
var migrated = JsonSerializer.Deserialize<SettingV2>(legacyJson, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/DiscriminatorPropertyNameSample.cs#L22-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-discriminator_property_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The property name change applies to all migratable types registered with the same `JsonSerializerOptions`. Use a name that doesn't conflict with your domain properties.

## Deriving discriminators from an existing attribute

If your domain model already has an attribute that uniquely identifies types (e.g., a `[SchemaVersion]` attribute), use `GetTypeDiscriminatorFrom<TAttribute>()` to derive discriminators from it instead of adding `TypeDiscriminator` to every `[JsonMigratable]`:

<!-- snippet: derive_discriminator -->
<a id='snippet-derive_discriminator'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.GetTypeDiscriminatorFrom<SchemaVersionAttribute>(
        attr => attr.Version);
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CustomDiscriminatorSample.cs#L59-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-derive_discriminator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The attribute must have a property or field that returns a string. The library reads this value at configuration time and uses it as the type discriminator.
>
> **Tip:** In Orleans projects, you can use `GetTypeDiscriminatorFrom<AliasAttribute>(attr => attr.Alias)` to derive discriminators from Orleans' `[Alias]` attribute. This avoids duplicating discriminator values and gives you compile-time uniqueness checks via the Orleans analyzer. See [Using Orleans `[Alias]` as the type discriminator](integration-patterns.md#using-orleans-alias-as-the-type-discriminator) for a full example.
