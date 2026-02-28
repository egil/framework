using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public class NestedMigratableBehaviorTests
{
    [Fact]
    public void Child_top_level_legacy_payload_migrates()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """
            {
              "$type":"child-v1",
              "name":"Egil Hansen"
            }
            """;

        var result = JsonSerializer.Deserialize<NestedChildV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil Hansen", result.FullName);
    }

    [Fact]
    public void Parent_current_payload_with_legacy_child_migrates_child()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """
            {
              "$type":"parent-v2",
              "child":{
                "$type":"child-v1",
                "name":"Egil Hansen"
              }
            }
            """;

        var result = JsonSerializer.Deserialize<NestedParentV2>(json, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Child);
        Assert.Equal("Egil Hansen", result.Child.FullName);
    }

    [Fact]
    public void Serialize_parent_with_child_writes_child_discriminator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new NestedParentV2(new NestedChildV2("Egil Hansen")), options);

        Assert.Contains("\"$type\":\"parent-v2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"child\":{\"$type\":\"child-v2\"", json, StringComparison.Ordinal);
    }
}

[JsonMigratable(TypeDiscriminator = "child-v1")]
public record class NestedChildV1(string Name);

[JsonMigratable(TypeDiscriminator = "child-v2")]
public record class NestedChildV2(string? FullName) : IMigrateFrom<NestedChildV1, NestedChildV2>
{
    public static bool TryMigrateFrom(NestedChildV1 source, out NestedChildV2 result)
    {
        result = new NestedChildV2(source.Name);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "parent-v2")]
public record class NestedParentV2(NestedChildV2 Child);
