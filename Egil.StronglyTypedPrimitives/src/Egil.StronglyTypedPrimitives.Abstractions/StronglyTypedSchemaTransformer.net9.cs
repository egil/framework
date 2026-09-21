#if NET9_0

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

public sealed partial class StronglyTypedSchemaTransformer
{
    // System.Text.Json's schema exporter attaches these value patterns to numbers under ASP.NET
    // Core's default JSON options (JsonNumberHandling.AllowReadingFromString). ASP.NET Core 9
    // replaces them with a format for the primitives in its own type/format table, but keeps
    // them for every other number, so the wrappers of those numbers must carry the same pattern.
    private const string IntegerPattern = @"^-?(?:0|[1-9]\d*)$";
    private const string NumberPattern = @"^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$";

    // ASP.NET Core 9 offers no way to ask the OpenAPI pipeline for another type's schema
    // (OpenApiSchemaTransformerContext.GetOrCreateSchemaAsync arrived in .NET 10), so the
    // type/format pairs the framework emits for each primitive are replicated here. Primitives
    // outside the framework's own type/format table (sbyte, Int128, UInt128, Half, TimeSpan and
    // Version) are documented with a value pattern instead of a format, replicated as well.
    private static readonly Dictionary<Type, (string Type, string? Format, string? Pattern)> SchemaByPrimitive = new()
    {
        [typeof(bool)] = ("boolean", null, null),
        [typeof(byte)] = ("integer", "uint8", null),
        [typeof(byte[])] = ("string", "byte", null),
        [typeof(sbyte)] = ("integer", null, IntegerPattern),
        [typeof(short)] = ("integer", "int16", null),
        [typeof(ushort)] = ("integer", "uint16", null),
        [typeof(int)] = ("integer", "int32", null),
        [typeof(uint)] = ("integer", "uint32", null),
        [typeof(long)] = ("integer", "int64", null),
        [typeof(ulong)] = ("integer", "uint64", null),
        [typeof(Int128)] = ("integer", null, IntegerPattern),
        [typeof(UInt128)] = ("integer", null, IntegerPattern),
        [typeof(Half)] = ("number", null, NumberPattern),
        [typeof(float)] = ("number", "float", null),
        [typeof(double)] = ("number", "double", null),
        [typeof(decimal)] = ("number", "double", null),
        [typeof(char)] = ("string", "char", null),
        [typeof(string)] = ("string", null, null),
        [typeof(Guid)] = ("string", "uuid", null),
        [typeof(Uri)] = ("string", "uri", null),
        [typeof(Version)] = ("string", null, @"^\d+(\.\d+){1,3}$"),
        [typeof(DateTime)] = ("string", "date-time", null),
        [typeof(DateTimeOffset)] = ("string", "date-time", null),
        [typeof(DateOnly)] = ("string", "date", null),
        [typeof(TimeOnly)] = ("string", "time", null),
        [typeof(TimeSpan)] = ("string", null, @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$"),
    };

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if ((GetPrimitiveType(context.JsonTypeInfo.Type) ?? GetParameterPrimitiveType(context)) is { } primitiveType
            && SchemaByPrimitive.TryGetValue(primitiveType, out var primitiveSchema))
        {
            schema.Type = primitiveSchema.Type;
            schema.Format = primitiveSchema.Format;
            schema.Pattern = primitiveSchema.Pattern;
            schema.Properties.Clear();
            schema.Required.Clear();
        }
        else if (GetElementPrimitive(context.JsonTypeInfo) is var (elementPrimitiveType, elementAdmitsNull)
            && SchemaByPrimitive.TryGetValue(elementPrimitiveType, out var elementSchema))
        {
            var element = new OpenApiSchema { Type = elementSchema.Type, Format = elementSchema.Format, Pattern = elementSchema.Pattern, Nullable = elementAdmitsNull };

            if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Dictionary)
            {
                schema.AdditionalProperties ??= element;
            }
            else
            {
                schema.Items ??= element;
            }
        }

        return Task.CompletedTask;
    }
}

#endif