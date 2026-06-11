namespace Egil.SystemTextJson.Migration.Samples.NestedObjects;

#region nested_child_types
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
#endregion

#region nested_parent_current
[JsonMigratable(TypeDiscriminator = "person-v2")]
public record class PersonV2(string Name, AddressV2 Address);
#endregion

#region nested_parent_both_migrate
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
#endregion

#region nested_nonmigratable_parent
public record class Company(string Name, AddressV2 HeadquartersAddress);
#endregion

#region nested_collection_types
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
#endregion

public sealed class NestedObjectsSampleTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    [Fact]
    public void Nested_child_migrated_inside_current_parent()
    {
        #region nested_child_migration_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Parent is current (v2), but child is old (v1)
        var json = """
            {
              "$type":"person-v2",
              "name":"Jane Doe",
              "address":{
                "$type":"address-v1",
                "fullAddress":"123 Main St, Springfield"
              }
            }
            """;

        var person = JsonSerializer.Deserialize<PersonV2>(json, options);
        #endregion

        Assert.NotNull(person);
        Assert.Equal("Jane Doe", person.Name);
        Assert.Equal("123 Main St", person.Address.Street);
        Assert.Equal("Springfield", person.Address.City);
    }

    [Fact]
    public void Both_parent_and_child_migrated()
    {
        #region nested_both_migrate_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Both parent (v1) and child (v1) need migration
        var json = """
            {
              "$type":"person-v1",
              "fullName":"Jane Doe",
              "address":{
                "$type":"address-v1",
                "fullAddress":"123 Main St, Springfield"
              }
            }
            """;

        var person = JsonSerializer.Deserialize<PersonV3>(json, options);
        #endregion

        Assert.NotNull(person);
        Assert.Equal("Jane", person.FirstName);
        Assert.Equal("Doe", person.LastName);
        Assert.Equal("123 Main St", person.Address.Street);
        Assert.Equal("Springfield", person.Address.City);
    }

    [Fact]
    public void Non_migratable_parent_with_migratable_child()
    {
        #region nested_nonmigratable_parent_usage
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
        #endregion

        Assert.NotNull(company);
        Assert.Equal("Acme Corp", company.Name);
        Assert.Equal("456 Oak Ave", company.HeadquartersAddress.Street);
        Assert.Equal("Metropolis", company.HeadquartersAddress.City);
    }

    [Fact]
    public void Collection_items_migrated()
    {
        #region nested_collection_usage
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
        #endregion

        Assert.NotNull(tags);
        Assert.Equal(3, tags.Count);
        Assert.Equal("breaking-news", tags[0].Slug);
        Assert.Equal("tech", tags[1].Slug);
        Assert.Equal("open-source", tags[2].Slug);
    }

    [Fact]
    public void Dictionary_values_migrated()
    {
        #region nested_dictionary_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """
            {
              "hq":{"$type":"address-v1","fullAddress":"123 Main St, Springfield"},
              "branch":{"$type":"address-v2","street":"789 Elm St","city":"Shelbyville"}
            }
            """;

        var offices = JsonSerializer.Deserialize<Dictionary<string, AddressV2>>(json, options);
        #endregion

        Assert.NotNull(offices);
        Assert.Equal("123 Main St", offices["hq"].Street);
        Assert.Equal("789 Elm St", offices["branch"].Street);
    }
}
