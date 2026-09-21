// See StronglyTypedJsonConverter.cs for why this only exists from net8.0 up.
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// A <see cref="JsonConverterFactory"/> that serializes every strongly typed primitive with
/// <see cref="StronglyTypedJsonConverter{TSelf, TPrimitive}"/>.
/// </summary>
/// <remarks>
/// Register it in <see cref="JsonSerializerOptions.Converters"/> when serializing through a
/// <see cref="JsonSerializerContext"/> on a JIT runtime; the System.Text.Json source generator
/// cannot see the <see cref="JsonConverterAttribute"/> the generator emits, but it does consult
/// the runtime converter list. The factory closes the generic converter with reflection, so for
/// trimmed or AOT compiled applications declare the attribute on the type instead.
/// </remarks>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Closes StronglyTypedJsonConverter<,> over value types at runtime. Declare [JsonConverter(typeof(StronglyTypedJsonConverter<TSelf, TPrimitive>))] on the strongly typed primitive instead when compiling ahead of time.")]
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Inspects the interfaces of the strongly typed primitive at runtime. Declare [JsonConverter(typeof(StronglyTypedJsonConverter<TSelf, TPrimitive>))] on the strongly typed primitive instead when trimming.")]
public sealed class StronglyTypedJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => FindPrimitiveType(typeToConvert) is not null;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var primitiveType = FindPrimitiveType(typeToConvert)
            ?? throw new ArgumentException($"{typeToConvert} is not a strongly typed primitive.", nameof(typeToConvert));

        var converterType = typeof(StronglyTypedJsonConverter<,>).MakeGenericType(typeToConvert, primitiveType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    // The primitive type is the second argument of IStronglyTypedPrimitive<TSelf, TPrimitive>. Only
    // the interface closed over the type itself counts, so a type that happens to implement the
    // interface for another strongly typed primitive is not mistaken for one.
    private static Type? FindPrimitiveType(Type type)
    {
        if (!type.IsValueType || !typeof(IStronglyTypedPrimitive).IsAssignableFrom(type))
        {
            return null;
        }

        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType
                && @interface.GetGenericTypeDefinition() == typeof(IStronglyTypedPrimitive<,>)
                && @interface.GetGenericArguments() is [var self, var primitive]
                && self == type)
            {
                return primitive;
            }
        }

        return null;
    }
}
#endif