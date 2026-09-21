// The transformer exists in the .NET assets only: netstandard2.0 has no Microsoft.AspNetCore.OpenApi.
#if NET10_0_OR_GREATER

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// Documents every <see cref="IStronglyTypedPrimitive{TPrimitiveType}"/> with the OpenAPI schema of the
/// primitive it wraps, so a strongly typed value appears in the document as the JSON it actually
/// serializes to. Register it with <c>options.AddSchemaTransformer&lt;StronglyTypedSchemaTransformer&gt;()</c>.
/// </summary>
public sealed class StronglyTypedSchemaTransformer : IOpenApiSchemaTransformer
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

    // The trim analyzer flags GetInterfaces() because the type arrives unannotated from
    // JsonTypeInfo.Type, JsonTypeInfo.ElementType or ApiParameterDescription.Type, and annotating
    // the parameter would only move the warning to those call sites. The call is nevertheless safe:
    // only IStronglyTypedPrimitive<T> implementations matter, every one of them carries a generated
    // [JsonConverter] whose converter and TryParse code use the interface's static abstract members,
    // so the trimmer keeps the interface map of any wrapper that reaches the OpenAPI pipeline. An
    // IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)) check per primitive would satisfy the
    // analyzer while depending on exactly the same metadata at runtime.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Only IStronglyTypedPrimitive<T> implementations are inspected, and their interface metadata is rooted by the generated JSON converter and TryParse code of every wrapper that reaches the OpenAPI pipeline.")]
    private static Type? GetPrimitiveType(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;

        foreach (var implemented in candidate.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IStronglyTypedPrimitive<>))
            {
                return implemented.GetGenericArguments()[0];
            }
        }

        return null;
    }

    // ASP.NET Core emits "items"/"additionalProperties" only when the element type produces a JSON
    // schema of its own. A strongly typed primitive serializes through a custom converter, which
    // System.Text.Json's schema exporter reports as the boolean schema "true", and that leaves the
    // collection schema without an element schema at all. Nothing is left for the framework's
    // recursive transformer pass to visit, so the element schema has to be created here.
    //
    // Because that schema starts from nothing, it must also say whether the element admits null.
    // GetPrimitiveType unwraps a Nullable<T> element on its way to the primitive, so the answer is
    // taken from the declared element type before it is unwrapped.
    private static (Type PrimitiveType, bool AdmitsNull)? GetElementPrimitive(JsonTypeInfo typeInfo)
        => typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
            && typeInfo.ElementType is { } elementType
            && GetPrimitiveType(elementType) is { } primitiveType
            ? (primitiveType, Nullable.GetUnderlyingType(elementType) is not null)
            : null;

    // Route, query and header parameters are bound through TryParse, and for such types ASP.NET Core
    // documents the parameter as a plain string: transformers receive the type info for string, not
    // for the strongly typed primitive. The parameter description still carries the declared type,
    // which is the only way to document the parameter like its primitive would be documented.
    private static Type? GetParameterPrimitiveType(OpenApiSchemaTransformerContext context)
        => context.JsonTypeInfo.Type == typeof(string) && context.ParameterDescription is { } parameter
            ? GetPrimitiveType(parameter.Type)
            : null;
}

#endif