namespace Egil.SystemTextJson.Migration.Samples.ErrorDiagnostics;

[JsonMigratable(TypeDiscriminator = "item-v1")]
public record class ItemV1(string Name);

[JsonMigratable(TypeDiscriminator = "item-v2")]
public record class ItemV2(string Name, string Slug) : IMigrateFrom<ItemV1, ItemV2>
{
    public static bool TryMigrateFrom(ItemV1 source, out ItemV2 result)
    {
        result = new ItemV2(source.Name, source.Name.ToLowerInvariant().Replace(' ', '-'));
        return true;
    }
}

public sealed class ErrorDiagnosticsSampleTests
{
    [Fact]
    public void Unknown_discriminator_throws_json_exception()
    {
        #region error_unknown_discriminator
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // "product-v99" is not registered as a source type for ItemV2
        var json = """{"$type":"product-v99","name":"Widget"}""";

        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ItemV2>(json, options));
        // ex.Message contains details about the unrecognized discriminator
        #endregion

        Assert.Contains("product-v99", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_discriminator_throws_dedicated_exception()
    {
        #region error_duplicate_discriminator
        // Two different source types resolve to the same discriminator value.
        // This is detected when the first deserialization attempt is made.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder
                .RegisterMigrator<DupMigratorA>()
                .RegisterMigrator<DupMigratorB>());

        var json = """{"$type":"dup-src","data":"test"}""";

        var ex = Assert.Throws<JsonMigrationDuplicateTypeDiscriminatorException>(
            () => JsonSerializer.Deserialize<DupTarget>(json, options));
        // ex.Discriminator == "dup-src"
        // ex.TargetType == typeof(DupTarget)
        #endregion

        Assert.Equal("dup-src", ex.Discriminator);
        Assert.Equal(typeof(DupTarget), ex.TargetType);
    }

    [Fact]
    public void Unmapped_member_handling_disallow_works_with_migration()
    {
        #region error_unmapped_member_handling
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.AddJsonMigrationSupport();

        // Even with strict unmapped-member checks, migration works correctly.
        // The $type discriminator is consumed by the library and does not leak
        // as an unmapped member on the target type.
        var json = """{"$type":"item-v1","name":"Widget"}""";

        var item = JsonSerializer.Deserialize<ItemV2>(json, options);
        #endregion

        Assert.NotNull(item);
        Assert.Equal("Widget", item.Name);
        Assert.Equal("widget", item.Slug);
    }
}

#region error_duplicate_discriminator_types
[JsonMigratable(TypeDiscriminator = "dup-src")]
public record class DupSourceA(string Data);

[JsonMigratable(TypeDiscriminator = "dup-src")]
public record class DupSourceB(string Data);

[JsonMigratable]
public record class DupTarget(string Data);

public class DupMigratorA : IMigrate<DupSourceA, DupTarget>
{
    public bool TryMigrateFrom(DupSourceA source, out DupTarget result)
    {
        result = new DupTarget(source.Data);
        return true;
    }
}

public class DupMigratorB : IMigrate<DupSourceB, DupTarget>
{
    public bool TryMigrateFrom(DupSourceB source, out DupTarget result)
    {
        result = new DupTarget(source.Data);
        return true;
    }
}
#endregion
