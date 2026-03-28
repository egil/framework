using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

public class EdgeCaseBehaviorTests
{
    [Fact]
    public void Deserialize_list_of_migratable_types_without_migration()
    {
        var options = CreateOptions();
        var list = new List<ListItemV2>
        {
            new("Egil", "Hansen"),
            new("Jane", "Doe"),
        };

        var json = JsonSerializer.Serialize(list, options);
        var result = JsonSerializer.Deserialize<List<ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Egil", result[0].FirstName);
        Assert.Equal("Doe", result[1].LastName);
    }

    [Fact]
    public void Deserialize_list_of_migratable_types_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new List<ListItemV1> { new("Egil Hansen"), new("Jane Doe") },
            options);

        var result = JsonSerializer.Deserialize<List<ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Egil", result[0].FirstName);
        Assert.Equal("Hansen", result[0].LastName);
        Assert.Equal("Jane", result[1].FirstName);
        Assert.Equal("Doe", result[1].LastName);
    }

    [Fact]
    public void Deserialize_array_of_migratable_types_without_migration()
    {
        var options = CreateOptions();
        var array = new[] { new ListItemV2("Egil", "Hansen") };

        var json = JsonSerializer.Serialize(array, options);
        var result = JsonSerializer.Deserialize<ListItemV2[]>(json, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Egil", result[0].FirstName);
    }

    [Fact]
    public void Deserialize_array_of_migratable_types_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new[] { new ListItemV1("Egil Hansen") },
            options);

        var result = JsonSerializer.Deserialize<ListItemV2[]>(json, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Egil", result[0].FirstName);
        Assert.Equal("Hansen", result[0].LastName);
    }

    [Fact]
    public void Deserialize_dictionary_with_migratable_values_without_migration()
    {
        var options = CreateOptions();
        var dict = new Dictionary<string, ListItemV2>
        {
            ["user1"] = new("Egil", "Hansen"),
        };

        var json = JsonSerializer.Serialize(dict, options);
        var result = JsonSerializer.Deserialize<Dictionary<string, ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("user1"));
        Assert.Equal("Egil", result["user1"].FirstName);
    }

    [Fact]
    public void Deserialize_dictionary_with_migratable_values_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new Dictionary<string, ListItemV1> { ["user1"] = new("Egil Hansen") },
            options);

        var result = JsonSerializer.Deserialize<Dictionary<string, ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("user1"));
        Assert.Equal("Egil", result["user1"].FirstName);
        Assert.Equal("Hansen", result["user1"].LastName);
    }

    [Fact]
    public void Deserialize_null_json_value_for_nullable_migratable_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<ListItemV2?>("null", options);

        Assert.Null(result);
    }

    [Fact]
    public async Task Deserialize_async_stream_without_migration()
    {
        var options = CreateOptions();
        var data = new ListItemV2("Egil", "Hansen");
        var json = JsonSerializer.SerializeToUtf8Bytes(data, options);

        using var stream = new MemoryStream(json);
        var result = await JsonSerializer.DeserializeAsync<ListItemV2>(stream, options, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
        Assert.Equal("Hansen", result.LastName);
    }

    [Fact]
    public async Task Deserialize_async_stream_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.SerializeToUtf8Bytes(new ListItemV1("Egil Hansen"), options);

        using var stream = new MemoryStream(json);
        var result = await JsonSerializer.DeserializeAsync<ListItemV2>(stream, options, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
        Assert.Equal("Hansen", result.LastName);
    }

    [Fact]
    public void Deserialize_with_unmapped_member_handling_disallow()
    {
        var options = CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

        var data = new ListItemV2("Egil", "Hansen");
        var json = JsonSerializer.Serialize(data, options);
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
    }

    [Fact]
    public void Deserialize_with_unmapped_member_handling_disallow_and_migration()
    {
        var options = CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

        var json = JsonSerializer.Serialize(new ListItemV1("Egil Hansen"), options);
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
        Assert.Equal("Hansen", result.LastName);
    }

    [Fact]
    public void Deserialize_with_discriminator_not_first_property_treated_as_legacy()
    {
        var options = CreateOptions();

        // JSON with $type NOT as the first property — treated as legacy payload.
        var json = """{"firstName":"Egil","lastName":"Hansen","$type":"not-first"}""";
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
        Assert.Equal("Hansen", result.LastName);
    }

    [Fact]
    public void Deserialize_legacy_payload_without_discriminator()
    {
        var options = CreateOptions();

        var json = """{"firstName":"Egil","lastName":"Hansen"}""";
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.FirstName);
        Assert.Equal("Hansen", result.LastName);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    [JsonMigratable(TypeDiscriminator = "list-item-v1")]
    public record class ListItemV1(string Name);

    [JsonMigratable(TypeDiscriminator = "list-item-v2")]
    public record class ListItemV2(string FirstName, string LastName) : IMigrateFrom<ListItemV1, ListItemV2>
    {
        public static bool TryMigrateFrom(ListItemV1 source, out ListItemV2 result)
        {
            var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = new ListItemV2(
                names.Length > 0 ? names[0] : string.Empty,
                names.Length > 1 ? names[1] : string.Empty);
            return true;
        }
    }
}
