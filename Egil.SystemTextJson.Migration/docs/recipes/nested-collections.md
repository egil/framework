# Nested & Collection Scenarios

The library automatically handles migratable types wherever they appear in the object graph — nested properties, collection items, and dictionary values are all migrated transparently.

## Nested migratable child inside a current parent

When the parent type is up-to-date but contains a child property whose payload is an older version, the child is migrated automatically during parent deserialization:

<!-- snippet: nested_child_types -->
<a id='snippet-nested_child_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "address-v1")]
public record class AddressV1(string FullAddress);

[JsonMigratable(TypeDiscriminator = "address-v2")]
public record class AddressV2(string Street, string City) : IMigrateFrom<AddressV1, AddressV2>
{
    public static bool TryMigrateFrom(AddressV1 source, out AddressV2 result)
    {
        var parts = source.FullAddress.Split(',', StringSplitOptions.TrimEntries);
        result = new AddressV2(
            parts.Length > 0 ? parts[0] : string.Empty,
            parts.Length > 1 ? parts[1] : string.Empty);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L3-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_child_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: nested_parent_current -->
<a id='snippet-nested_parent_current'></a>
```cs
[JsonMigratable(TypeDiscriminator = "person-v2")]
public record class PersonV2(string Name, AddressV2 Address);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L21-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_parent_current' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: nested_child_migration_usage -->
<a id='snippet-nested_child_migration_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Parent is current (v2), but child is old (v1)
var json = """
    {
      "$type":"person-v2",
      "name":"Egil Hansen",
      "address":{
        "$type":"address-v1",
        "fullAddress":"123 Main St, Springfield"
      }
    }
    """;

var person = JsonSerializer.Deserialize<PersonV2>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L77-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_child_migration_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The child type must have `[JsonMigratable]` and a migration path defined. The parent needs no special configuration beyond its own `[JsonMigratable]` attribute.

## Both parent and child need migration

When both the parent and child payloads are older versions, both are migrated in a single deserialization call:

<!-- snippet: nested_parent_both_migrate -->
<a id='snippet-nested_parent_both_migrate'></a>
```cs
[JsonMigratable(TypeDiscriminator = "person-v1")]
public record class PersonV1(string FullName, AddressV2 Address);

[JsonMigratable(TypeDiscriminator = "person-v3")]
public record class PersonV3(string FirstName, string LastName, AddressV2 Address)
    : IMigrateFrom<PersonV1, PersonV3>
{
    public static bool TryMigrateFrom(PersonV1 source, out PersonV3 result)
    {
        var names = source.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new PersonV3(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Address);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L26-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_parent_both_migrate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: nested_both_migrate_usage -->
<a id='snippet-nested_both_migrate_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Both parent (v1) and child (v1) need migration
var json = """
    {
      "$type":"person-v1",
      "fullName":"Egil Hansen",
      "address":{
        "$type":"address-v1",
        "fullAddress":"123 Main St, Springfield"
      }
    }
    """;

var person = JsonSerializer.Deserialize<PersonV3>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L105-L122' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_both_migrate_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** The library handles the migration order automatically — the child is migrated first (as part of its own deserialization), then the parent migrator receives the already-migrated child.

## Migratable child inside a non-migratable parent

The parent type does not need `[JsonMigratable]` for its children to be migrated. As long as migration support is enabled on the `JsonSerializerOptions`, any migratable child property is handled:

<!-- snippet: nested_nonmigratable_parent -->
<a id='snippet-nested_nonmigratable_parent'></a>
```cs
public record class Company(string Name, AddressV2 HeadquartersAddress);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L46-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_nonmigratable_parent' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: nested_nonmigratable_parent_usage -->
<a id='snippet-nested_nonmigratable_parent_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Company has no [JsonMigratable], but its Address child does.
var json = """
    {
      "name":"Acme Corp",
      "headquartersAddress":{
        "$type":"address-v1",
        "fullAddress":"456 Oak Ave, Metropolis"
      }
    }
    """;

var company = JsonSerializer.Deserialize<Company>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L134-L150' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_nonmigratable_parent_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This is useful when the parent type is a simple container (e.g., a DTO or wrapper) that doesn't need versioning itself.

## Migrating items in collections

Collections of migratable types — `List<T>`, `T[]`, `Dictionary<string, T>` — work transparently. Each element is inspected and migrated independently:

<!-- snippet: nested_collection_types -->
<a id='snippet-nested_collection_types'></a>
```cs
[JsonMigratable(TypeDiscriminator = "tag-v1")]
public record class TagV1(string Label);

[JsonMigratable(TypeDiscriminator = "tag-v2")]
public record class TagV2(string Name, string Slug) : IMigrateFrom<TagV1, TagV2>
{
    public static bool TryMigrateFrom(TagV1 source, out TagV2 result)
    {
        result = new TagV2(source.Label, source.Label.ToLowerInvariant().Replace(' ', '-'));
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L50-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_collection_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**List/array:**

<!-- snippet: nested_collection_usage -->
<a id='snippet-nested_collection_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

// Array of old tags — each is migrated independently
var json = """
    [
      {"$type":"tag-v1","label":"Breaking News"},
      {"$type":"tag-v2","name":"Tech","slug":"tech"},
      {"$type":"tag-v1","label":"Open Source"}
    ]
    """;

var tags = JsonSerializer.Deserialize<List<TagV2>>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L161-L175' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_collection_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Dictionary values:**

<!-- snippet: nested_dictionary_usage -->
<a id='snippet-nested_dictionary_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();

var json = """
    {
      "hq":{"$type":"address-v1","fullAddress":"123 Main St, Springfield"},
      "branch":{"$type":"address-v2","street":"789 Elm St","city":"Shelbyville"}
    }
    """;

var offices = JsonSerializer.Deserialize<Dictionary<string, AddressV2>>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/NestedObjectsSample.cs#L187-L199' title='Snippet source file'>snippet source</a> | <a href='#snippet-nested_dictionary_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Mixed collections are supported — some items can be current-version while others need migration. Each item is handled individually based on its `$type` discriminator.
