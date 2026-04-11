namespace Egil.SystemTextJson.Migration.Samples.SourceGen;

[JsonMigratable(TypeDiscriminator = "user-v1")]
public record UserV1(string Name, int Age);

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

#region source_gen_context
[JsonSerializable(typeof(UserV1))]
[JsonSerializable(typeof(UserV2))]
public partial class AppJsonContext : JsonSerializerContext;
#endregion

public class SourceGenTests
{
    [Fact]
    public void Source_gen_context_with_migration()
    {
        #region source_gen_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain.Add(AppJsonContext.Default);
        #endregion

        var json = """{"$type":"user-v1","name":"Jane Doe","age":30}""";
        var user = JsonSerializer.Deserialize<UserV2>(json, options)!;

        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Equal(30, user.Age);
    }
}
