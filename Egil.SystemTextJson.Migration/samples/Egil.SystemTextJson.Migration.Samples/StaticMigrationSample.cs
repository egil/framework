namespace Egil.SystemTextJson.Migration.Samples.StaticMigration;

[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

#region static_migration_type
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IMigrateFrom<UserV1, UserV2>
{
    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
#endregion

public class StaticMigrationTests
{
    [Fact]
    public void Static_migration_converts_v1_to_v2()
    {
        #region static_migration_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // A UserV1 payload is automatically migrated to UserV2:
        var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
        UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
        // user is UserV2 { FirstName = "Jane", LastName = "Doe", Age = 30 }
        #endregion

        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Equal(30, user.Age);
    }
}
