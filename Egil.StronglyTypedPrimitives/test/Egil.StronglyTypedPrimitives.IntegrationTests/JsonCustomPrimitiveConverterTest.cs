using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Examples;

namespace Egil.StronglyTypedPrimitives;

public class JsonCustomPrimitiveConverterTest
{
    private static readonly InvoiceDto Invoice = new(
        new StronglyTypedIntWithExplicitJsonConverter(42),
        new Dictionary<StronglyTypedIntWithExplicitJsonConverter, string> { [new(1)] = "a" });

    [Fact]
    public void Context_without_metadata_for_the_primitive_uses_a_converter_registered_in_the_options()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = InvoiceJsonContext.Default,
            Converters = { new HexIntConverter() },
        };

        Assert.Null(InvoiceJsonContext.Default.GetTypeInfo(typeof(int)));

        var json = JsonSerializer.Serialize(Invoice, options);

        Assert.Equal("""{"Id":"0x2a","Lines":{"1":"a"}}""", json);
        Assert.Equal(Invoice, JsonSerializer.Deserialize<InvoiceDto>(json, options));
    }

    [Fact]
    public void Context_without_metadata_for_the_primitive_and_no_registered_converter_uses_the_built_in_one()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = InvoiceJsonContext.Default };

        var json = JsonSerializer.Serialize(Invoice, options);

        Assert.Equal("""{"Id":42,"Lines":{"1":"a"}}""", json);
        Assert.Equal(Invoice, JsonSerializer.Deserialize<InvoiceDto>(json, options));
    }

    // Writes ints as "0x2a" so the test can tell a custom converter apart from the built-in one.
    private sealed class HexIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => int.Parse(reader.GetString()!.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteStringValue("0x" + value.ToString("x", CultureInfo.InvariantCulture));
    }
}