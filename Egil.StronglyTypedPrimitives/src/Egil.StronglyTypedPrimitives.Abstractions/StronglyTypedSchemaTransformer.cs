#if NET9_0_OR_GREATER

using Microsoft.AspNetCore.OpenApi;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// Documents every <see cref="IStronglyTypedPrimitive{TPrimitiveType}"/> with the OpenAPI schema of the
/// primitive it wraps, so a strongly typed value appears in the document as the JSON it actually
/// serializes to. Register it with <c>options.AddSchemaTransformer&lt;StronglyTypedSchemaTransformer&gt;()</c>.
/// </summary>
public sealed partial class StronglyTypedSchemaTransformer : IOpenApiSchemaTransformer
{
    // The trim analyzer cannot see that only IStronglyTypedPrimitive<T> implementations matter here.
    // A type reaches the OpenAPI pipeline because an endpoint serializes or binds it, and the generated
    // JsonConverter/TryParse code calls that interface's static abstract members, so the trimmer keeps
    // the implementation. Checking IsAssignableTo(typeof(IStronglyTypedPrimitive<int>)) per primitive
    // would satisfy the analyzer while depending on exactly the same interface metadata at runtime.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Only IStronglyTypedPrimitive<T> implementations are inspected, and those are rooted by the generated converter and TryParse code of every type that reaches the OpenAPI pipeline.")]
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
    private static Type? GetElementPrimitiveType(JsonTypeInfo typeInfo)
        => typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary && typeInfo.ElementType is { } elementType
            ? GetPrimitiveType(elementType)
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