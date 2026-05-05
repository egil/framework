namespace Egil.SystemTextJson.Migration.Samples.LegacyPayloads;

[JsonMigratable(TypeDiscriminator = "order-v1")]
public record class OrderV1(string Item, int Qty);

[JsonMigratable(TypeDiscriminator = "order-v2")]
public record class OrderV2(string ItemName, int Quantity)
    : IMigrateFrom<OrderV1, OrderV2>
{
    public static bool TryMigrateFrom(OrderV1 source, out OrderV2 result)
    {
        result = new OrderV2(source.Item, source.Qty);
        return true;
    }
}

public record class CustomerNameV0(string FirstName, string LastName);

#region legacy_undiscriminated_source_type
[JsonMigratable(
    TypeDiscriminator = "customer-name-v1",
    UndiscriminatedSourceType = typeof(CustomerNameV0))]
public record class CustomerNameV1(string Name)
    : IMigrateFrom<CustomerNameV0, CustomerNameV1>
{
    public static bool TryMigrateFrom(CustomerNameV0 source, out CustomerNameV1 result)
    {
        result = new CustomerNameV1($"{source.FirstName} {source.LastName}");
        return true;
    }
}
#endregion

public sealed class LegacyPayloadSampleTests
{
    [Fact]
    public void Legacy_payload_without_discriminator()
    {
        #region legacy_no_discriminator
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Existing stored JSON — written before migration support was added.
        // No $type property at all. The library treats it as the target type directly.
        var json = """{"itemName":"Widget","quantity":5}""";

        var order = JsonSerializer.Deserialize<OrderV2>(json, options);
        // Works perfectly — existing data keeps working with zero changes.
        #endregion

        Assert.NotNull(order);
        Assert.Equal("Widget", order.ItemName);
        Assert.Equal(5, order.Quantity);
    }

    [Fact]
    public void Discriminator_not_first_treated_as_legacy()
    {
        #region legacy_discriminator_not_first
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // The $type property exists but is NOT the first property.
        // The library only checks the first property for the discriminator,
        // so this is treated as a legacy payload and deserialized as-is.
        var json = """{"itemName":"Widget","quantity":5,"$type":"order-v2"}""";

        var order = JsonSerializer.Deserialize<OrderV2>(json, options);
        #endregion

        Assert.NotNull(order);
        Assert.Equal("Widget", order.ItemName);
        Assert.Equal(5, order.Quantity);
    }

    [Fact]
    public void Undiscriminated_source_payload_is_migrated()
    {
        #region legacy_undiscriminated_source_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Existing stored JSON was written before migration support existed,
        // so it has no $type discriminator. CustomerNameV1 opts in to treating
        // discriminator-less objects as CustomerNameV0 and runs its migrator.
        var json = """{"firstName":"Jane","lastName":"Doe"}""";

        CustomerNameV1 customer = JsonSerializer.Deserialize<CustomerNameV1>(json, options)!;
        // customer is CustomerNameV1 { Name = "Jane Doe" }
        #endregion

        Assert.Equal("Jane Doe", customer.Name);
    }
}
