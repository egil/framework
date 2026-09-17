using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// The JSON token family a migrator source type deserializes from when the
/// payload is not a JSON object. Computed once per migrator so the read path
/// only compares enum values.
/// </summary>
internal enum SourceValueShape
{
    /// <summary>The shape is not a primitive token family (objects, collections, custom types).</summary>
    Unknown,
    String,
    Number,
    Boolean,
}

internal static class SourceValueShapes
{
    /// <summary>
    /// Classifies a CLR type by the JSON primitive token STJ reads it from.
    /// </summary>
    public static SourceValueShape Classify(Type type)
    {
        // Nullable<T> is read by T's converter, so classify the underlying type.
        type = Nullable.GetUnderlyingType(type) ?? type;

        // STJ writes char as a one-character JSON string.
        if (type == typeof(string) || type == typeof(char))
        {
            return SourceValueShape.String;
        }

        if (type == typeof(bool))
        {
            return SourceValueShape.Boolean;
        }

        // The TypeCode fast path keeps enums (whose TypeCode is their underlying integral
        // type) classified as numbers, matching STJ's default enum serialization.
        if (Type.GetTypeCode(type) is
            TypeCode.Byte or TypeCode.SByte or
            TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double or
            TypeCode.Decimal)
        {
            return SourceValueShape.Number;
        }

        // Every other numeric STJ supports (Half, Int128, UInt128, BFloat16, Decimal32/64/128,
        // and future additions) has TypeCode.Object but implements INumberBase<TSelf>, so
        // the interface check keeps this list-free across runtime versions. char also
        // implements INumberBase<char> but is handled as a string above.
        return ImplementsNumberBase(type) ? SourceValueShape.Number : SourceValueShape.Unknown;
    }

    public static bool IsTokenCompatible(JsonTokenType tokenType, SourceValueShape shape)
    {
        return tokenType switch
        {
            JsonTokenType.String => shape is SourceValueShape.String,
            JsonTokenType.Number => shape is SourceValueShape.Number,
            JsonTokenType.True or JsonTokenType.False => shape is SourceValueShape.Boolean,
            _ => false,
        };
    }

    /// <summary>
    /// Returns the element type of an enumerable source or the value type of a dictionary
    /// source, used to disambiguate collection migrators by their first element.
    /// </summary>
    public static Type GetValueType(Type collectionType, JsonTypeInfoKind kind)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()!;
        }

        // For generic collections (List<T>, Dictionary<K,V>, etc.),
        // the element type is the last generic argument.
        if (collectionType.IsGenericType)
        {
            Type[] args = collectionType.GetGenericArguments();
            return kind is JsonTypeInfoKind.Dictionary ? args[^1] : args[0];
        }

        return typeof(object);
    }

    private static bool ImplementsNumberBase(Type type)
    {
        if (!type.IsValueType)
        {
            return false;
        }

        foreach (Type @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType
                && @interface.GetGenericTypeDefinition() == typeof(System.Numerics.INumberBase<>)
                && @interface.GetGenericArguments()[0] == type)
            {
                return true;
            }
        }

        return false;
    }
}
