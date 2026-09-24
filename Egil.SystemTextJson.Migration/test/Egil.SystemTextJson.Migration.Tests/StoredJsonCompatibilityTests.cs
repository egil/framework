using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public sealed class StoredJsonCompatibilityTests
{
    // Captured with the released STJM 1.7.4 package and JsonSerializerDefaults.Web.
    // Keep the payloads literal so a change to today's writer cannot conceal a read regression.
    private const string TrackedV1Json = """{"$type":"Egil.SystemTextJson.Migration.Tests.TrackingV1","name":"Jane Doe","age":42}""";
    private const string OldMixedJson = """{"$type":"old-mixed","csvValues":"one,two"}""";

    [Fact]
    public void Stored_v1_object_still_selects_its_migrator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<TrackingExternalMigrator>());
        options.TypeInfoResolverChain.Add(TrackingJsonContext.Default);

        var migrated = JsonSerializer.Deserialize<TrackingV3>(TrackedV1Json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Jane", migrated.FirstName);
        Assert.Equal("Doe", migrated.LastName);
        Assert.Equal(42, migrated.Age);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Stored_object_with_custom_discriminator_still_selects_its_migrator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var migrated = JsonSerializer.Deserialize<NonObjectPayloadMigrationTests.MixedState>(OldMixedJson, options);

        Assert.NotNull(migrated);
        Assert.Equal(["one", "two"], migrated.Values);
        Assert.Equal("from-object", migrated.Source);
    }
}
