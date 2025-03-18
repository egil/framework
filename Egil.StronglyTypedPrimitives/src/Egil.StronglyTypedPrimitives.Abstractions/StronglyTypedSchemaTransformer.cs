#if NET9_0_OR_GREATER

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Egil.StronglyTypedPrimitives;

public sealed class StronglyTypedSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly Type IStronglyTypedPrimitiveType = typeof(IStronglyTypedPrimitive);

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type.IsAssignableTo(IStronglyTypedPrimitiveType))
        {
            schema.Type = "string";
            schema.Properties.Clear();
        }

        return Task.CompletedTask;
    }
}

#endif