#if NET9_0_OR_GREATER

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Egil.StronglyTypedPrimitives;

public sealed class StronglyTypedSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type != "array" && !context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive)))
        {
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive)) == false)
        {
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<bool>)))
        {
            schema.Type = "boolean";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<bool>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "boolean";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte>)))
        {
            schema.Type = "integer";
            schema.Format = "uint8";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "uint8";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte[]>)))
        {
            schema.Type = "string";
            schema.Format = "byte";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<byte[]>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "byte";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "int32";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<uint>)))
        {
            schema.Type = "integer";
            schema.Format = "uint32";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<uint>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "uint32";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<long>)))
        {
            schema.Type = "integer";
            schema.Format = "int64";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<long>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "int64";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<ulong>)))
        {
            schema.Type = "integer";
            schema.Format = "uint64";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<ulong>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "uint64";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<short>)))
        {
            schema.Type = "integer";
            schema.Format = "int16";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<short>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "int16";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<ushort>)))
        {
            schema.Type = "integer";
            schema.Format = "uint16";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<ushort>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "integer";
            schema.Items.Format = "uint16";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<float>)))
        {
            schema.Type = "number";
            schema.Format = "float";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<float>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "number";
            schema.Items.Format = "float";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<double>)))
        {
            schema.Type = "number";
            schema.Format = "double";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<double>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "number";
            schema.Items.Format = "double";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<decimal>)))
        {
            schema.Type = "number";
            schema.Format = "double";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<decimal>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "number";
            schema.Items.Format = "double";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTime>)) ||
            context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTimeOffset>)))
        {
            schema.Type = "string";
            schema.Format = "date-time";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" &&
            (context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTime>)) == true ||
             context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateTimeOffset>)) == true))
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "date-time";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<Guid>)))
        {
            schema.Type = "string";
            schema.Format = "uuid";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<Guid>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "uuid";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<char>)))
        {
            schema.Type = "string";
            schema.Format = "char";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<char>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "char";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<Uri>)))
        {
            schema.Type = "string";
            schema.Format = "uri";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<Uri>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "uri";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<string>)))
        {
            schema.Type = "string";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<string>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<TimeOnly>)))
        {
            schema.Type = "string";
            schema.Format = "time";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<TimeOnly>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "time";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateOnly>)))
        {
            schema.Type = "string";
            schema.Format = "date";
            schema.Properties.Clear();
            return Task.CompletedTask;
        }

        if (schema.Type == "array" && context.JsonTypeInfo.ElementType?.IsAssignableTo(typeof(IStronglyTypedPrimitive<DateOnly>)) == true)
        {
            schema.Items ??= new OpenApiSchema();
            schema.Items.Type = "string";
            schema.Items.Format = "date";
            schema.Items.Properties.Clear();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
#endif