using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

public class LibraryCoreBehaviorTests
{
    private readonly JsonSerializerOptions options;

    public LibraryCoreBehaviorTests()
    {
        options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder => builder.RegisterMigrator<CoreSampleMigrator>());
        options.TypeInfoResolverChain.Add(CoreBehaviorJsonContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    }

    [Fact]
    public void Migrate_with_static_and_registered_external_migrators()
    {
        var v1 = new CoreSampleV1("Egil Hansen", 42);
        var json = JsonSerializer.Serialize(v1, options);

        var migrated = JsonSerializer.Deserialize<CoreSampleV3>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Egil", migrated.FirstName);
        Assert.Equal("Hansen", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Serialize_writes_type_discriminator_first()
    {
        var v2 = new CoreSampleV2("Egil", "Hansen", 42);

        var json = JsonSerializer.Serialize(v2, options);

        Assert.StartsWith("{\"$type\":", json, StringComparison.Ordinal);
    }
}

[JsonMigratable]
public record class CoreSampleV1(string Name, int Age);

[JsonMigratable]
public record class CoreSampleV2(string FirstName, string LastName, int Age)
{
    public static bool TryMigrateFrom(CoreSampleV1 source, out CoreSampleV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new CoreSampleV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }
}

[JsonMigratable]
public record class CoreSampleV3(string FirstName, string LastName, int Age);

public class CoreSampleMigrator :
    IMigrate<CoreSampleV1, CoreSampleV3>,
    IMigrate<CoreSampleV2, CoreSampleV3>
{
    public bool TryMigrateFrom(CoreSampleV1 source, out CoreSampleV3 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new CoreSampleV3(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age);
        return true;
    }

    public bool TryMigrateFrom(CoreSampleV2 source, out CoreSampleV3 result)
    {
        result = new CoreSampleV3(source.FirstName, source.LastName, source.Age);
        return true;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(CoreSampleV1))]
[JsonSerializable(typeof(CoreSampleV2))]
[JsonSerializable(typeof(CoreSampleV3))]
public partial class CoreBehaviorJsonContext : JsonSerializerContext;
