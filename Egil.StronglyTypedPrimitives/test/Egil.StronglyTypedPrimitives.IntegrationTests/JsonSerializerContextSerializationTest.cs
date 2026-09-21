using System.Text.Json;
using System.Text.Json.Serialization;
using Examples;

namespace Egil.StronglyTypedPrimitives;

public class JsonSerializerContextSerializationTest
{
    private static readonly OrderDto Order = new(
        new StronglyTypedInt(7),
        new StronglyTypedDecimal(1.5m),
        new Dictionary<StronglyTypedGuid, string> { [new(Guid.Parse("0d2f2d9a-1b7e-4d2a-9d3c-5f1f0a6c4e21"))] = "fragile" });

    [Fact]
    public void Context_with_factory_registered_serializes_like_reflection()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = OrderJsonContext.Default,
            Converters = { new StronglyTypedJsonConverterFactory() },
        };

        var json = JsonSerializer.Serialize(Order, options);

        Assert.Equal(JsonSerializer.Serialize(Order), json);
        Assert.Equal(Order, JsonSerializer.Deserialize<OrderDto>(json, options));
    }

    [Fact]
    public void Context_with_user_declared_converter_attribute_serializes_like_reflection()
    {
        var invoice = new InvoiceDto(
            new StronglyTypedIntWithExplicitJsonConverter(42),
            new Dictionary<StronglyTypedIntWithExplicitJsonConverter, string> { [new(1)] = "a" });

        var json = JsonSerializer.Serialize(invoice, InvoiceJsonContext.Default.InvoiceDto);

        Assert.Equal("""{"Id":42,"Lines":{"1":"a"}}""", json);
        Assert.Equal(JsonSerializer.Serialize(invoice), json);
        Assert.Equal(invoice, JsonSerializer.Deserialize(json, InvoiceJsonContext.Default.InvoiceDto));
    }
}

public sealed record OrderDto(StronglyTypedInt Id, StronglyTypedDecimal Price, Dictionary<StronglyTypedGuid, string> Tags)
{
    public bool Equals(OrderDto? other)
        => other is not null && Id == other.Id && Price == other.Price && Tags.SequenceEqual(other.Tags);

    public override int GetHashCode() => HashCode.Combine(Id, Price);
}

public sealed record InvoiceDto(StronglyTypedIntWithExplicitJsonConverter Id, Dictionary<StronglyTypedIntWithExplicitJsonConverter, string> Lines)
{
    public bool Equals(InvoiceDto? other)
        => other is not null && Id == other.Id && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => Id.GetHashCode();
}

[JsonSerializable(typeof(OrderDto))]
internal sealed partial class OrderJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InvoiceDto))]
internal sealed partial class InvoiceJsonContext : JsonSerializerContext;