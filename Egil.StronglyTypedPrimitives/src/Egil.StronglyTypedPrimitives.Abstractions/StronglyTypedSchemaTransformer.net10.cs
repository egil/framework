#if NET10_0

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Egil.StronglyTypedPrimitives;


public sealed class StronglyTypedSchemaTransformer3 : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type != JsonSchemaType.Array && !context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive)))
        {
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive)) == false)
        {
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<bool>)))
        {
            schema.Type = JsonSchemaType.Boolean;
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<bool>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Boolean,
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "uint8";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "uint8",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte[]>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "byte";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte[]>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "byte",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "int32";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<uint>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "uint32";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<uint>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "uint32",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<long>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "int64";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<long>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "int64",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<ulong>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "uint64";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<ulong>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "uint64",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<short>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "int16";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<short>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "int16",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<ushort>)))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "uint16";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<ushort>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Integer,
                Format = "uint16",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<float>)))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "float";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<float>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Number,
                Format = "float",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<double>)))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "double";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<double>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Number,
                Format = "double",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<decimal>)))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "double";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<decimal>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.Number,
                Format = "double",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTime>)) ||
            context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTimeOffset>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array &&
            (context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTime>)) == true ||
             context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTimeOffset>)) == true))
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "date-time",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<Guid>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<Guid>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "uuid",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<char>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "char";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<char>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "char",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<Uri>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uri";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<Uri>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "uri",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<string>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<string>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<TimeOnly>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "time";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<TimeOnly>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "time",
            };
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateOnly>)))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date";
            schema.Properties?.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == JsonSchemaType.Array && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateOnly>)) == true)
        {
            schema.Items = new OpenApiSchema()
            {
                Type = JsonSchemaType.String,
                Format = "date",
            };
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
#endif