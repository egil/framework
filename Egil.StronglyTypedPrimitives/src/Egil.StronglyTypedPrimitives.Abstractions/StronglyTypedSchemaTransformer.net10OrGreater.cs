#if NET10_0_OR_GREATER

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

public sealed partial class StronglyTypedSchemaTransformer
{
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if ((GetPrimitiveType(context.JsonTypeInfo.Type) ?? GetParameterPrimitiveType(context)) is { } primitiveType)
        {
            var primitiveSchema = await GetPrimitiveSchemaAsync(context, primitiveType, cancellationToken).ConfigureAwait(false);
            ApplyPrimitiveSchema(schema, primitiveSchema);
        }
        else if (GetElementPrimitive(context.JsonTypeInfo) is var (elementPrimitiveType, elementAdmitsNull))
        {
            var primitiveSchema = await GetPrimitiveSchemaAsync(context, elementPrimitiveType, cancellationToken).ConfigureAwait(false);
            var element = new OpenApiSchema { Type = elementAdmitsNull ? JsonSchemaType.Null : null };
            ApplyPrimitiveSchema(element, primitiveSchema);

            if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Dictionary)
            {
                schema.AdditionalProperties ??= element;
            }
            else
            {
                schema.Items ??= element;
            }
        }
    }

    // Asking the pipeline for the primitive's schema keeps the strongly typed wrapper in sync with
    // whatever ASP.NET Core emits for that primitive (type, format and any value pattern), instead
    // of hard-coding the pairs here. The parameter description is deliberately not forwarded: it
    // describes the wrapper, whose TryParse support would make the framework answer "string" again.
    private static Task<OpenApiSchema> GetPrimitiveSchemaAsync(OpenApiSchemaTransformerContext context, Type primitiveType, CancellationToken cancellationToken)
        => context.GetOrCreateSchemaAsync(primitiveType, parameterDescription: null, cancellationToken);

    private static void ApplyPrimitiveSchema(OpenApiSchema target, OpenApiSchema primitive)
    {
        // Whether this position admits null has already been decided, by the framework (for example
        // an optional query parameter) or by the element schema created above; the primitive's own
        // schema never admits null, so carry the flag over.
        var admitsNull = target.Type?.HasFlag(JsonSchemaType.Null) == true;
        target.Type = admitsNull ? primitive.Type | JsonSchemaType.Null : primitive.Type;
        target.Format = primitive.Format ?? target.Format;
        // A pattern already on the target comes from the property's own validation attributes
        // (for example [RegularExpression]) or from an earlier transformer, and describes this
        // use of the wrapper more precisely than the primitive's generic value pattern does.
        target.Pattern ??= primitive.Pattern;
        target.Minimum = primitive.Minimum ?? target.Minimum;
        target.Maximum = primitive.Maximum ?? target.Maximum;

        // The wrapper is a record struct, so without the converter it would be documented as an
        // object with the primitive as a "Value" property; that shape never appears on the wire.
        target.Properties = null;
        target.Required = null;
    }
}

#endif