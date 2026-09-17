using Examples;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace Egil.StronglyTypedPrimitives;

public sealed class StronglyTypedSchemaTransformerTest(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    public static TheoryData<string, string, string?> Primitives => new()
    {
        { "stringValue", "string", null },
        { "byteValue", "integer", "uint8" },
        { "shortValue", "integer", "int16" },
        { "uShortValue", "integer", "uint16" },
        { "intValue", "integer", "int32" },
        { "uIntValue", "integer", "uint32" },
        { "longValue", "integer", "int64" },
        { "uLongValue", "integer", "uint64" },
        { "floatValue", "number", "float" },
        { "doubleValue", "number", "double" },
        { "decimalValue", "number", "double" },
        { "dateTimeValue", "string", "date-time" },
        { "dateTimeOffsetValue", "string", "date-time" },
        { "dateOnlyValue", "string", "date" },
        { "timeOnlyValue", "string", "time" },
        { "guidValue", "string", "uuid" },
    };

    [Theory]
    [MemberData(nameof(Primitives))]
    public void Property_is_documented_like_its_primitive(string property, string type, string? format)
    {
        var stronglyTyped = fixture.PropertySchema<StronglyTypedScalars>(property);
        var primitive = fixture.PropertySchema<PlainScalars>(property);

        AssertPrimitiveSchema(stronglyTyped, type, format);
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    [Fact]
    public void Nullable_value_type_property_admits_null_and_is_documented_like_its_primitive()
    {
        var schema = fixture.PropertySchema<StronglyTypedScalars>("nullableIntValue");

        AssertNullablePrimitiveSchema(schema, "integer", "int32");
    }

    [Fact]
    public void Nullable_reference_type_property_admits_null_and_is_documented_like_its_primitive()
    {
        var schema = fixture.PropertySchema<StronglyTypedScalars>("nullableStringValue");

        AssertNullablePrimitiveSchema(schema, "string", null);
    }

    [Theory]
    [InlineData("intArray", "integer", "int32")]
    [InlineData("stringList", "string", null)]
    [InlineData("guidEnumerable", "string", "uuid")]
    public void Collection_items_are_documented_like_the_primitive(string property, string type, string? format)
    {
        var stronglyTyped = fixture.PropertySchema<StronglyTypedCollections>(property);
        var primitive = fixture.PropertySchema<PlainCollections>(property);

        Assert.Equal("array", stronglyTyped["type"]?.GetValue<string>());
        AssertPrimitiveSchema(stronglyTyped["items"]!, type, format);
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    [Theory]
    [InlineData("intByKey", "integer", "int32")]
    [InlineData("stringByKey", "string", null)]
    public void Dictionary_values_are_documented_like_the_primitive(string property, string type, string? format)
    {
        var stronglyTyped = fixture.PropertySchema<StronglyTypedCollections>(property);
        var primitive = fixture.PropertySchema<PlainCollections>(property);

        Assert.Equal("object", stronglyTyped["type"]?.GetValue<string>());
        AssertPrimitiveSchema(stronglyTyped["additionalProperties"]!, type, format);
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    [Theory]
    [InlineData("int", "integer", "int32")]
    [InlineData("guid", "string", "uuid")]
    [InlineData("string", "string", null)]
    [InlineData("date-only", "string", "date")]
    public void Route_parameter_is_documented_like_its_primitive(string name, string type, string? format)
    {
        var stronglyTyped = fixture.ParameterSchema($"/by-{name}/{{id}}");
        var primitive = fixture.ParameterSchema($"/by-plain-{name}/{{id}}");

        AssertPrimitiveSchema(stronglyTyped, type, format);
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    [Fact]
    public void Query_parameter_is_documented_like_its_primitive()
    {
        var stronglyTyped = fixture.ParameterSchema("/by-int-query");
        var primitive = fixture.ParameterSchema("/by-plain-int-query");

        AssertPrimitiveSchema(stronglyTyped, "integer", "int32");
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    [Fact]
    public void Response_body_is_documented_like_its_primitive()
    {
        var stronglyTyped = fixture.ResponseSchema("/by-int/{id}");
        var primitive = fixture.ResponseSchema("/by-plain-int/{id}");

        AssertPrimitiveSchema(stronglyTyped, "integer", "int32");
        Assert.Equal(primitive.ToJsonString(), stronglyTyped.ToJsonString());
    }

    // ASP.NET Core documents numbers as accepting both the JSON number and its string form
    // (JsonSerializerDefaults.Web reads numbers from strings) on .NET 10 and later, so "type" is
    // either a single string or an array that must contain the expected type.
    private static void AssertPrimitiveSchema(JsonNode schema, string type, string? format)
    {
        var types = schema["type"] is JsonArray alternatives
            ? alternatives.Select(alternative => alternative!.GetValue<string>())
            : [schema["type"]!.GetValue<string>()];

        Assert.Contains(type, types);
        Assert.Equal(format, schema["format"]?.GetValue<string>());
        Assert.Null(schema["properties"]);
    }

    // OpenAPI 3.0 (.NET 9) marks the schema itself with "nullable": true, while 3.1 and later
    // (.NET 10 onwards) wrap the wrapper's reference in oneOf: [{ "type": "null" }, { "$ref": ... }].
    private void AssertNullablePrimitiveSchema(JsonNode schema, string type, string? format)
    {
        if (schema["oneOf"] is JsonArray alternatives)
        {
            Assert.Contains(alternatives, alternative => alternative?["type"]?.GetValue<string>() == "null");
            var value = Assert.Single(alternatives, alternative => alternative?["type"]?.GetValue<string>() != "null");
            AssertPrimitiveSchema(fixture.Resolve(value!), type, format);
        }
        else
        {
            Assert.True(schema["nullable"]?.GetValue<bool>());
            AssertPrimitiveSchema(schema, type, format);
        }
    }
}

public sealed record StronglyTypedScalars(
    StronglyTypedString StringValue,
    StronglyTypedByte ByteValue,
    StronglyTypedShort ShortValue,
    StronglyTypedUShort UShortValue,
    StronglyTypedInt IntValue,
    StronglyTypedUInt UIntValue,
    StronglyTypedLong LongValue,
    StronglyTypedULong ULongValue,
    StronglyTypedFloat FloatValue,
    StronglyTypedDouble DoubleValue,
    StronglyTypedDecimal DecimalValue,
    StronglyTypedDateTime DateTimeValue,
    StronglyTypedDateTimeOffset DateTimeOffsetValue,
    StronglyTypedDateOnly DateOnlyValue,
    StronglyTypedTimeOnly TimeOnlyValue,
    StronglyTypedGuid GuidValue,
    StronglyTypedInt? NullableIntValue,
    StronglyTypedString? NullableStringValue);

public sealed record PlainScalars(
    string StringValue,
    byte ByteValue,
    short ShortValue,
    ushort UShortValue,
    int IntValue,
    uint UIntValue,
    long LongValue,
    ulong ULongValue,
    float FloatValue,
    double DoubleValue,
    decimal DecimalValue,
    DateTime DateTimeValue,
    DateTimeOffset DateTimeOffsetValue,
    DateOnly DateOnlyValue,
    TimeOnly TimeOnlyValue,
    Guid GuidValue,
    int? NullableIntValue,
    string? NullableStringValue);

public sealed record StronglyTypedCollections(
    StronglyTypedInt[] IntArray,
    List<StronglyTypedString> StringList,
    IEnumerable<StronglyTypedGuid> GuidEnumerable,
    Dictionary<string, StronglyTypedInt> IntByKey,
    IReadOnlyDictionary<string, StronglyTypedString> StringByKey);

public sealed record PlainCollections(
    int[] IntArray,
    List<string> StringList,
    IEnumerable<Guid> GuidEnumerable,
    Dictionary<string, int> IntByKey,
    IReadOnlyDictionary<string, string> StringByKey);

/// <summary>
/// Boots a minimal API host once per test class with <see cref="StronglyTypedSchemaTransformer"/>
/// registered, and exposes the OpenAPI document it serves. Every strongly typed endpoint has a
/// "plain" twin that uses the underlying primitive, so tests can assert that the two schemas are identical.
/// </summary>
public sealed class OpenApiDocumentFixture : IAsyncLifetime
{
    private WebApplication? app;
    private JsonNode document = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi(options => options.AddSchemaTransformer<StronglyTypedSchemaTransformer>());

        app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/strongly-typed-scalars", (StronglyTypedScalars body) => body);
        app.MapPost("/plain-scalars", (PlainScalars body) => body);
        app.MapPost("/strongly-typed-collections", (StronglyTypedCollections body) => body);
        app.MapPost("/plain-collections", (PlainCollections body) => body);
        app.MapGet("/by-int/{id}", (StronglyTypedInt id) => id);
        app.MapGet("/by-plain-int/{id}", (int id) => id);
        app.MapGet("/by-guid/{id}", (StronglyTypedGuid id) => id);
        app.MapGet("/by-plain-guid/{id}", (Guid id) => id);
        app.MapGet("/by-string/{id}", (StronglyTypedString id) => id);
        app.MapGet("/by-plain-string/{id}", (string id) => id);
        app.MapGet("/by-date-only/{id}", (StronglyTypedDateOnly id) => id);
        app.MapGet("/by-plain-date-only/{id}", (DateOnly id) => id);
        app.MapGet("/by-int-query", (StronglyTypedInt id) => id);
        app.MapGet("/by-plain-int-query", (int id) => id);
        await app.StartAsync();

        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        document = JsonNode.Parse(json)!;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }
    }

    public JsonNode PropertySchema<TBody>(string property)
        => Resolve(document["components"]!["schemas"]![typeof(TBody).Name]!["properties"]![property]!);

    public JsonNode ParameterSchema(string path)
        => Resolve(document["paths"]![path]!["get"]!["parameters"]![0]!["schema"]!);

    public JsonNode ResponseSchema(string path)
        => Resolve(document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!);

    // Complex and strongly typed schemas are emitted once under components and referenced as
    // { "$ref": "#/components/schemas/StronglyTypedInt" } wherever they are used.
    public JsonNode Resolve(JsonNode schema)
        => schema["$ref"] is { } reference
            ? document["components"]!["schemas"]![reference.GetValue<string>().Split('/')[^1]]!
            : schema;
}