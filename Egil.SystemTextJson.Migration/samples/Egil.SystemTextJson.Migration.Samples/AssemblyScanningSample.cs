namespace Egil.SystemTextJson.Migration.Samples.AssemblyScanning;

[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

[JsonMigratable(TypeDiscriminator = "user-v2")]
public record UserV2(string FirstName, string LastName, int Age);

public class UserMigrator : IMigrate<UserV1, UserV2>
{
    public bool TryMigrateFrom(UserV1 source, out UserV2 result)
    {
        var names = source.Name.Split(' ');
        result = new UserV2(names[0], names.ElementAtOrDefault(1) ?? "", source.Age);
        return true;
    }
}

public class AssemblyScanningTests
{
    [Fact]
    public void Assembly_scanning_discovers_migrators()
    {
        #region assembly_scanning
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder =>
        {
            builder.RegisterMigratorsFromAssemblies(typeof(UserMigrator).Assembly);
        });
        #endregion

        var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
        var user = JsonSerializer.Deserialize<UserV2>(json, options)!;

        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Equal(30, user.Age);
    }
}
