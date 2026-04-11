namespace Egil.SystemTextJson.Migration.Samples.MigrationTracking;

[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

#region migration_tracking_type
[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age)
    : IJsonMigrationTracked, IMigrateFrom<UserV1, UserV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}
#endregion

public class MigrationTrackingTests
{
    [Fact]
    public void Migrated_instance_has_tracking_flag_set()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        #region migration_tracking_usage
        // After deserialization:
        var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
        UserV2 user = JsonSerializer.Deserialize<UserV2>(json, options)!;
        if (user.MigratedDuringDeserialization)
        {
            // Persist the updated representation so future reads
            // hit the happy path.
            // await SaveAsync(user);
        }
        #endregion

        Assert.True(user.MigratedDuringDeserialization);
        Assert.Equal("Jane", user.FirstName);
    }

    [Fact]
    public void Current_version_has_tracking_flag_unset()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"$type":"user-v2","firstName":"Jane","lastName":"Doe","age":30}""";
        var user = JsonSerializer.Deserialize<UserV2>(json, options)!;

        Assert.False(user.MigratedDuringDeserialization);
    }
}
