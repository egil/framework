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

        // Built-in STJ converters that read from a JSON string token. char is written as a
        // one-character string and byte[] as base64. Converter overrides (for example
        // JsonStringEnumConverter) are not visible here; enums stay numeric.
        if (type == typeof(string)
            || type == typeof(char)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Uri)
            || type == typeof(Version)
            || type == typeof(byte[]))
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

        // The remaining numerics with built-in STJ converters have TypeCode.Object. This is an
        // explicit allowlist rather than an INumberBase<TSelf> check because BigInteger and
        // Complex implement that interface without having a converter, and routing to them
        // would fail inside STJ instead of falling through.
        if (type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128)
#if NET11_0_OR_GREATER
            || type == typeof(System.Numerics.BFloat16)
            || type == typeof(System.Numerics.Decimal32)
            || type == typeof(System.Numerics.Decimal64)
            || type == typeof(System.Numerics.Decimal128)
#endif
            )
        {
            return SourceValueShape.Number;
        }

        return SourceValueShape.Unknown;
    }

    /// <param name="allowQuotedNumbers">
    /// Whether a JSON string may also satisfy a numeric shape, as with
    /// <see cref="System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString"/>.
    /// Callers try exact shapes first so a string-shaped candidate always wins string tokens.
    /// </param>
    public static bool IsTokenCompatible(JsonTokenType tokenType, SourceValueShape shape, bool allowQuotedNumbers = false)
    {
        return tokenType switch
        {
            JsonTokenType.String => shape is SourceValueShape.String || (allowQuotedNumbers && shape is SourceValueShape.Number),
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
}
