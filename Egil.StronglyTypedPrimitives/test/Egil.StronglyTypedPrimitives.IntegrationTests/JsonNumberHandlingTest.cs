using System.Text.Json;
using System.Text.Json.Serialization;
using Examples;

namespace Egil.StronglyTypedPrimitives;

public class JsonNumberHandlingTest
{
    [Fact]
    public void Web_defaults_read_a_quoted_number_into_an_int_primitive()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Equal(new StronglyTypedInt(42), JsonSerializer.Deserialize<StronglyTypedInt>("\"42\"", options));
    }

    [Fact]
    public void Web_defaults_read_a_quoted_number_into_a_dto_property()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var dto = JsonSerializer.Deserialize<QuantityDto>("""{"quantity":"42"}""", options);

        Assert.Equal(new QuantityDto(new StronglyTypedInt(42)), dto);
    }

    [Fact]
    public void Default_options_reject_a_quoted_number()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StronglyTypedInt>("\"42\""));
    }

    [Fact]
    public void WriteAsString_writes_an_int_primitive_as_a_quoted_number()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString };

        Assert.Equal("\"42\"", JsonSerializer.Serialize(new StronglyTypedInt(42), options));
    }

    [Fact]
    public void Context_without_metadata_for_the_primitive_falls_back_to_the_built_in_converter()
    {
        var invoice = new InvoiceDto(new(42), new Dictionary<StronglyTypedIntWithExplicitJsonConverter, string> { [new(1)] = "a" });

        Assert.Null(InvoiceJsonContext.Default.GetTypeInfo(typeof(int)));

        var json = JsonSerializer.Serialize(invoice, InvoiceJsonContext.Default.InvoiceDto);

        Assert.Equal("""{"Id":42,"Lines":{"1":"a"}}""", json);
        Assert.Equal(invoice, JsonSerializer.Deserialize(json, InvoiceJsonContext.Default.InvoiceDto));
    }
}

public sealed record QuantityDto(StronglyTypedInt Quantity);