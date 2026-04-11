namespace Egil.SystemTextJson.Migration.Samples.CustomDiscriminator;

#region custom_discriminator_attribute
// Per-type via the attribute:
[JsonMigratable(
    TypeDiscriminator = "user-v2",
    TypeDiscriminatorPropertyName = "version")]
public record UserV2(string FirstName, string LastName, int Age);
#endregion

public class CustomDiscriminatorTests
{
    [Fact]
    public void Custom_discriminator_property_name_via_attribute()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"version":"user-v2","firstName":"Jane","lastName":"Doe","age":30}""";
        var user = JsonSerializer.Deserialize<UserV2>(json, options)!;

        Assert.Equal("Jane", user.FirstName);
    }

    [Fact]
    public void Custom_discriminator_property_name_via_builder()
    {
        #region custom_discriminator_builder
        // Or set a global default property name via the builder:
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder =>
        {
            builder.SetTypeDiscriminatorPropertyName("_schema");
        });
        #endregion

        var json = """{"_schema":"user-v2","firstName":"Jane","lastName":"Doe","age":30}""";
        var user = JsonSerializer.Deserialize<UserV2>(json, options)!;

        Assert.Equal("Jane", user.FirstName);
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class SchemaVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}

[SchemaVersion("config-v1")]
[JsonMigratable]
public record ConfigV1(string Setting);

public class DeriveDiscriminatorTests
{
    [Fact]
    public void Derive_discriminator_from_existing_attribute()
    {
        #region derive_discriminator
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder =>
        {
            builder.GetTypeDiscriminatorFrom<SchemaVersionAttribute>(
                attr => attr.Version);
        });
        #endregion

        var json = """{"$type":"config-v1","setting":"value"}""";
        var config = JsonSerializer.Deserialize<ConfigV1>(json, options)!;

        Assert.Equal("value", config.Setting);
    }
}
