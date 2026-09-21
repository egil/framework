#if NET9_0

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

public sealed partial class StronglyTypedSchemaTransformer
{
    // ASP.NET Core 9 offers no way to ask the OpenAPI pipeline for another type's schema
    // (OpenApiSchemaTransformerContext.GetOrCreateSchemaAsync arrived in .NET 10), so the
    // type/format pairs the framework emits for each primitive are replicated here.
    private static readonly Dictionary<Type, (string Type, string? Format)> SchemaByPrimitive = new()
    {
        [typeof(bool)] = ("boolean", null),
        [typeof(byte)] = ("integer", "uint8"),
        [typeof(byte[])] = ("string", "byte"),
        [typeof(short)] = ("integer", "int16"),
        [typeof(ushort)] = ("integer", "uint16"),
        [typeof(int)] = ("integer", "int32"),
        [typeof(uint)] = ("integer", "uint32"),
        [typeof(long)] = ("integer", "int64"),
        [typeof(ulong)] = ("integer", "uint64"),
        [typeof(float)] = ("number", "float"),
        [typeof(double)] = ("number", "double"),
        [typeof(decimal)] = ("number", "double"),
        [typeof(char)] = ("string", "char"),
        [typeof(string)] = ("string", null),
        [typeof(Guid)] = ("string", "uuid"),
        [typeof(Uri)] = ("string", "uri"),
        [typeof(DateTime)] = ("string", "date-time"),
        [typeof(DateTimeOffset)] = ("string", "date-time"),
        [typeof(DateOnly)] = ("string", "date"),
        [typeof(TimeOnly)] = ("string", "time"),
    };

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if ((GetPrimitiveType(context.JsonTypeInfo.Type) ?? GetParameterPrimitiveType(context)) is { } primitiveType
            && SchemaByPrimitive.TryGetValue(primitiveType, out var primitiveSchema))
        {
            schema.Type = primitiveSchema.Type;
            schema.Format = primitiveSchema.Format;
            schema.Properties.Clear();
            schema.Required.Clear();
        }
        else if (GetElementPrimitive(context.JsonTypeInfo) is var (elementPrimitiveType, elementAdmitsNull)
            && SchemaByPrimitive.TryGetValue(elementPrimitiveType, out var elementSchema))
        {
            var element = new OpenApiSchema { Type = elementSchema.Type, Format = elementSchema.Format, Nullable = elementAdmitsNull };

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