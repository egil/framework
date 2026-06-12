using Examples;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
#if NET9_0
using Microsoft.OpenApi.Models;
#elif NET10_0
using Microsoft.OpenApi;
#endif
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

public class StronglyTypedSchemaTransformerTest
{
    // Microsoft.OpenApi v1 (net9.0) models schema types as strings, while v2 (net10.0)
    // uses the JsonSchemaType enum. These constants let the test bodies stay identical
    // across both TFMs.
#if NET9_0
    private const string StringType = "string";
    private const string IntegerType = "integer";
    private const string NumberType = "number";
    private const string ArrayType = "array";
#elif NET10_0
    private const JsonSchemaType StringType = JsonSchemaType.String;
    private const JsonSchemaType IntegerType = JsonSchemaType.Integer;
    private const JsonSchemaType NumberType = JsonSchemaType.Number;
    private const JsonSchemaType ArrayType = JsonSchemaType.Array;
#endif

    [Fact]
    public async Task String_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedString));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_string_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedString[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Byte_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedByte));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("uint8", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_byte_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedByte[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("uint8", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Int_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedInt));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("int32", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_int_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedInt[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("int32", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task UInt_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUInt));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("uint32", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_uint_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUInt[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("uint32", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Long_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedLong));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("int64", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_long_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedLong[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("int64", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task ULong_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedULong));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("uint64", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_ulong_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedULong[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("uint64", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Short_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedShort));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("int16", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_short_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedShort[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("int16", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task UShort_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUShort));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(IntegerType, schema.Type);
        Assert.Equal("uint16", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_ushort_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUShort[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(IntegerType, schema.Items?.Type);
        Assert.Equal("uint16", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Float_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedFloat));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(NumberType, schema.Type);
        Assert.Equal("float", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_float_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedFloat[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(NumberType, schema.Items?.Type);
        Assert.Equal("float", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Double_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDouble));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(NumberType, schema.Type);
        Assert.Equal("double", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_double_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDouble[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(NumberType, schema.Items?.Type);
        Assert.Equal("double", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Decimal_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDecimal));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(NumberType, schema.Type);
        Assert.Equal("double", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_decimal_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDecimal[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(NumberType, schema.Items?.Type);
        Assert.Equal("double", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task DateTime_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTime));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        Assert.Equal("date-time", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_datetime_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTime[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        Assert.Equal("date-time", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task DateTimeOffset_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTimeOffset));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        Assert.Equal("date-time", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_datetimeoffset_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTimeOffset[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        Assert.Equal("date-time", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Guid_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedGuid));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        Assert.Equal("uuid", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_guid_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedGuid[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        Assert.Equal("uuid", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task TimeOnly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedTimeOnly));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        Assert.Equal("time", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_timeonly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedTimeOnly[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        Assert.Equal("time", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task DateOnly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateOnly));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(StringType, schema.Type);
        Assert.Equal("date", schema.Format);
        AssertNoProperties(schema);
    }

    [Fact]
    public async Task Array_of_dateonly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateOnly[]));
        var schema = new OpenApiSchema() { Type = ArrayType };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal(ArrayType, schema.Type);
        Assert.Equal(StringType, schema.Items?.Type);
        Assert.Equal("date", schema.Items?.Format);
        AssertNoProperties(schema);
    }

    // Properties is non-nullable in Microsoft.OpenApi v1 but nullable in v2, so accept both
    // null and empty as "no properties".
    private static void AssertNoProperties(OpenApiSchema schema)
        => Assert.True(schema.Properties is null or { Count: 0 });

    private static OpenApiSchemaTransformerContext CreateTransformerContext(Type jsonType) => new OpenApiSchemaTransformerContext
    {
        DocumentName = "TestDocument",
        ParameterDescription = null,
        JsonTypeInfo = JsonTypeInfo.CreateJsonTypeInfo(jsonType, JsonSerializerOptions.Default),
        JsonPropertyInfo = null,
        ApplicationServices = new ServiceCollection().BuildServiceProvider()
    };
}
