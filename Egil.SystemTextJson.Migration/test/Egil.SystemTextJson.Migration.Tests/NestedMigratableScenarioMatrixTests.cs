using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public class NestedMigratableScenarioMatrixTests
{
    [Fact]
    public void Parent_migratable_and_child_migratable_when_both_are_up_to_date_deserializes()
    {
        var result = Deserialize<ScenarioParentV2>(
            """
            {
              "$type":"scenario-parent-v2",
              "parentFirstName":"Egil",
              "parentLastName":"Hansen",
              "child":{
                "$type":"scenario-child-v2",
                "firstName":"Ada",
                "lastName":"Lovelace"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.ParentFirstName);
        Assert.Equal("Hansen", result.ParentLastName);
        Assert.NotNull(result.Child);
        Assert.Equal("Ada", result.Child.FirstName);
        Assert.Equal("Lovelace", result.Child.LastName);
    }

    [Fact]
    public void Parent_migratable_and_child_not_migratable_when_parent_is_up_to_date_deserializes()
    {
        var result = Deserialize<ScenarioParentWithPlainChildV2>(
            """
            {
              "$type":"scenario-parent-plain-v2",
              "parentFirstName":"Egil",
              "parentLastName":"Hansen",
              "child":{
                "value":"plain-child"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.ParentFirstName);
        Assert.Equal("Hansen", result.ParentLastName);
        Assert.NotNull(result.Child);
        Assert.Equal("plain-child", result.Child.Value);
    }

    [Fact]
    public void Parent_not_migratable_and_child_migratable_when_child_needs_migration_deserializes()
    {
        var result = Deserialize<ScenarioPlainParent>(
            """
            {
              "child":{
                "$type":"scenario-child-v1",
                "name":"Ada Lovelace"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.NotNull(result.Child);
        Assert.Equal("Ada", result.Child.FirstName);
        Assert.Equal("Lovelace", result.Child.LastName);
    }

    [Fact]
    public void Parent_needs_migration_child_is_up_to_date_migrates_parent_and_keeps_child()
    {
        var result = Deserialize<ScenarioParentV2>(
            """
            {
              "$type":"scenario-parent-v1",
              "parentName":"Egil Hansen",
              "child":{
                "$type":"scenario-child-v2",
                "firstName":"Ada",
                "lastName":"Lovelace"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.ParentFirstName);
        Assert.Equal("Hansen", result.ParentLastName);
        Assert.NotNull(result.Child);
        Assert.Equal("Ada", result.Child.FirstName);
        Assert.Equal("Lovelace", result.Child.LastName);
    }

    [Fact]
    public void Parent_is_up_to_date_child_needs_migration_migrates_child()
    {
        var result = Deserialize<ScenarioParentV2>(
            """
            {
              "$type":"scenario-parent-v2",
              "parentFirstName":"Egil",
              "parentLastName":"Hansen",
              "child":{
                "$type":"scenario-child-v1",
                "name":"Ada Lovelace"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.ParentFirstName);
        Assert.Equal("Hansen", result.ParentLastName);
        Assert.NotNull(result.Child);
        Assert.Equal("Ada", result.Child.FirstName);
        Assert.Equal("Lovelace", result.Child.LastName);
    }

    [Fact]
    public void Parent_and_child_need_migration_migrates_both()
    {
        var result = Deserialize<ScenarioParentV2>(
            """
            {
              "$type":"scenario-parent-v1",
              "parentName":"Egil Hansen",
              "child":{
                "$type":"scenario-child-v1",
                "name":"Ada Lovelace"
              }
            }
            """);

        Assert.NotNull(result);
        Assert.Equal("Egil", result.ParentFirstName);
        Assert.Equal("Hansen", result.ParentLastName);
        Assert.NotNull(result.Child);
        Assert.Equal("Ada", result.Child.FirstName);
        Assert.Equal("Lovelace", result.Child.LastName);
    }

    private static T Deserialize<T>(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        T? result = JsonSerializer.Deserialize<T>(json, options);
        Assert.NotNull(result);
        return result;
    }
}

[JsonMigratable(TypeDiscriminator = "scenario-child-v1")]
public record class ScenarioChildV1(string Name);

[JsonMigratable(TypeDiscriminator = "scenario-child-v2")]
public record class ScenarioChildV2(string? FirstName, string? LastName) : IMigrateFrom<ScenarioChildV1, ScenarioChildV2>
{
    public static bool TryMigrateFrom(ScenarioChildV1 source, out ScenarioChildV2 result)
    {
        string[] names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new ScenarioChildV2(
            names.Length > 0 ? names[0] : null,
            names.Length > 1 ? names[1] : null);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "scenario-parent-v1")]
public record class ScenarioParentV1(string ParentName, ScenarioChildV2 Child);

[JsonMigratable(TypeDiscriminator = "scenario-parent-v2")]
public record class ScenarioParentV2(string? ParentFirstName, string? ParentLastName, ScenarioChildV2 Child) : IMigrateFrom<ScenarioParentV1, ScenarioParentV2>
{
    public static bool TryMigrateFrom(ScenarioParentV1 source, out ScenarioParentV2 result)
    {
        string[] names = source.ParentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new ScenarioParentV2(
            names.Length > 0 ? names[0] : null,
            names.Length > 1 ? names[1] : null,
            source.Child);
        return true;
    }
}

public record class ScenarioPlainChild(string Value);

[JsonMigratable(TypeDiscriminator = "scenario-parent-plain-v1")]
public record class ScenarioParentWithPlainChildV1(string ParentName, ScenarioPlainChild Child);

[JsonMigratable(TypeDiscriminator = "scenario-parent-plain-v2")]
public record class ScenarioParentWithPlainChildV2(string? ParentFirstName, string? ParentLastName, ScenarioPlainChild Child) : IMigrateFrom<ScenarioParentWithPlainChildV1, ScenarioParentWithPlainChildV2>
{
    public static bool TryMigrateFrom(ScenarioParentWithPlainChildV1 source, out ScenarioParentWithPlainChildV2 result)
    {
        string[] names = source.ParentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new ScenarioParentWithPlainChildV2(
            names.Length > 0 ? names[0] : null,
            names.Length > 1 ? names[1] : null,
            source.Child);
        return true;
    }
}

public record class ScenarioPlainParent(ScenarioChildV2 Child);
