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
        var v1 = new CoreSampleV1("Jane Doe", 42);
        var json = JsonSerializer.Serialize(v1, options);

        var migrated = JsonSerializer.Deserialize<CoreSampleV3>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Jane", migrated.FirstName);
        Assert.Equal("Doe", migrated.LastName);
        Assert.Equal(42, migrated.Age);
    }

    [Fact]
    public void Serialize_writes_type_discriminator_first()
    {
        var v2 = new CoreSampleV2("Jane", "Doe", 42);

        var json = JsonSerializer.Serialize(v2, options);

        Assert.StartsWith("{\"$type\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_and_deserialize_without_migration()
    {
        var v1 = new CoreSampleV1("Jane", 42);

        var json = JsonSerializer.Serialize(v1, options);
        var result = JsonSerializer.Deserialize<CoreSampleV1>(json, options);

        Assert.Equal(v1, result);
    }

    [Fact]
    public void Struct_sources_migrate_to_struct_targets_through_both_contracts()
    {
        var structOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport(
            builder => builder.RegisterMigrator<CoreStructMigrator>());

        var staticResult = JsonSerializer.Deserialize<CoreStructStaticV2>("""{"$type":"core-struct-v1","value":41}""", structOptions);
        var externalResult = JsonSerializer.Deserialize<CoreStructExternalV2>("""{"$type":"core-struct-v1","value":41}""", structOptions);

        Assert.Equal(42, staticResult.Value);
        Assert.Equal(42, externalResult.Value);
    }

    [Fact]
    public void A_successful_migrator_returning_null_still_uses_failure_handling()
    {
        var nullResultOptions = new JsonSerializerOptions().AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CoreNullResultTarget>("""{"$type":"core-struct-v1","Value":41}""", nullResultOptions));
    }
}

[JsonMigratable]
public record class CoreSampleV1(string Name, int Age);

[JsonMigratable]
public record class CoreSampleV2(string FirstName, string LastName, int Age) :
    IMigrateFrom<CoreSampleV1, CoreSampleV2>
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

[JsonMigratable(TypeDiscriminator = "core-struct-v1")]
public readonly record struct CoreStructV1(int Value);

[JsonMigratable(TypeDiscriminator = "core-struct-static-v2")]
public readonly record struct CoreStructStaticV2(int Value) : IMigrateFrom<CoreStructV1, CoreStructStaticV2>
{
    public static bool TryMigrateFrom(CoreStructV1 source, out CoreStructStaticV2 result)
    {
        result = new(source.Value + 1);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "core-struct-external-v2")]
public readonly record struct CoreStructExternalV2(int Value);

public sealed class CoreStructMigrator : IMigrate<CoreStructV1, CoreStructExternalV2>
{
    public bool TryMigrateFrom(CoreStructV1 source, out CoreStructExternalV2 result)
    {
        result = new(source.Value + 1);
        return true;
    }
}

[JsonMigratable]
public sealed class CoreNullResultTarget : IMigrateFrom<CoreStructV1, CoreNullResultTarget>
{
    public static bool TryMigrateFrom(CoreStructV1 source, out CoreNullResultTarget result)
    {
        result = null!;
        return true;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(CoreSampleV1))]
[JsonSerializable(typeof(CoreSampleV2))]
[JsonSerializable(typeof(CoreSampleV3))]
public partial class CoreBehaviorJsonContext : JsonSerializerContext;
