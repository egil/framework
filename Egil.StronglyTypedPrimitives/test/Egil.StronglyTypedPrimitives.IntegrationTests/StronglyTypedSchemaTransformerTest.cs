using Examples;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

public class StronglyTypedSchemaTransformerTest
{
    [Fact]
    public async Task String_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedString));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_string_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedString[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Byte_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedByte));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("uint8", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_byte_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedByte[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("uint8", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Int_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedInt));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("int32", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_int_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedInt[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("int32", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task UInt_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUInt));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("uint32", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_uint_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUInt[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("uint32", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Long_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedLong));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("int64", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_long_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedLong[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("int64", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task ULong_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedULong));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("uint64", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_ulong_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedULong[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("uint64", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Short_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedShort));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("int16", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_short_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedShort[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("int16", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task UShort_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUShort));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("integer", schema.Type);
        Assert.Equal("uint16", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_ushort_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedUShort[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("integer", schema.Items.Type);
        Assert.Equal("uint16", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Float_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedFloat));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("number", schema.Type);
        Assert.Equal("float", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_float_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedFloat[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("number", schema.Items.Type);
        Assert.Equal("float", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Double_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDouble));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("number", schema.Type);
        Assert.Equal("double", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_double_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDouble[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("number", schema.Items.Type);
        Assert.Equal("double", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Decimal_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDecimal));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("number", schema.Type);
        Assert.Equal("double", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_decimal_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDecimal[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("number", schema.Items.Type);
        Assert.Equal("double", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task DateTime_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTime));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Equal("date-time", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_datetime_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTime[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Equal("date-time", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task DateTimeOffset_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTimeOffset));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Equal("date-time", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_datetimeoffset_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateTimeOffset[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Equal("date-time", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Guid_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedGuid));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Equal("uuid", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_guid_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedGuid[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Equal("uuid", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task TimeOnly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedTimeOnly));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Equal("time", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_timeonly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedTimeOnly[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Equal("time", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task DateOnly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateOnly));
        var schema = new OpenApiSchema();

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("string", schema.Type);
        Assert.Equal("date", schema.Format);
        Assert.Empty(schema.Properties);
    }

    [Fact]
    public async Task Array_of_dateonly_based_strongly_typed()
    {
        var sut = new StronglyTypedSchemaTransformer();
        var context = CreateTransformerContext(typeof(StronglyTypedDateOnly[]));
        var schema = new OpenApiSchema() { Type = "array" };

        await sut.TransformAsync(schema, context, TestContext.Current.CancellationToken);

        Assert.Equal("array", schema.Type);
        Assert.Equal("string", schema.Items.Type);
        Assert.Equal("date", schema.Items.Format);
        Assert.Empty(schema.Properties);
    }

    private static OpenApiSchemaTransformerContext CreateTransformerContext(Type jsonType) => new OpenApiSchemaTransformerContext
    {
        DocumentName = "TestDocument",
        ParameterDescription = null,
        JsonTypeInfo = JsonTypeInfo.CreateJsonTypeInfo(jsonType, JsonSerializerOptions.Default),
        JsonPropertyInfo = null,
        ApplicationServices = new ServiceCollection().BuildServiceProvider()
    };
}
