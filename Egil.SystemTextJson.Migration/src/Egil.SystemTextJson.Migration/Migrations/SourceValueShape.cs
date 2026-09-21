using System.Text.Json;
using System.Text.Json.Serialization;
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
        // one-character string and byte[], Memory<byte> and ReadOnlyMemory<byte> as base64. Converter overrides (for example
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
            || type == typeof(byte[])
            || type == typeof(Memory<byte>)
            || type == typeof(ReadOnlyMemory<byte>))
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

    /// <summary>
    /// Whether 1.x matched <paramref name="type"/> to a scalar token: <c>string</c>, <c>bool</c> and
    /// the <see cref="TypeCode"/> numerics (which include enums). These sources are matched ahead of
    /// the types 2.0 added, so a payload that 1.x routed keeps its source when a newer source of
    /// the same JSON shape is registered alongside it.
    /// </summary>
    public static bool IsLegacyShape(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(string)
            || type == typeof(bool)
            || Type.GetTypeCode(type) is
                TypeCode.Byte or TypeCode.SByte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double or
                TypeCode.Decimal;
    }

    /// <summary>
    /// Whether <see cref="JsonNumberHandling.AllowReadingFromString"/> lets the converter for
    /// <paramref name="type"/> read ordinary quoted numbers such as "42". Enums are excluded:
    /// their converter ignores number handling and reads strings only as names. The converter
    /// still validates the text, so routing only needs to know that a string may be a number.
    /// </summary>
    public static bool AllowsQuotedNumbers(JsonNumberHandling numberHandling, Type type)
        => (numberHandling & JsonNumberHandling.AllowReadingFromString) != 0
            && !(Nullable.GetUnderlyingType(type) ?? type).IsEnum;

    /// <summary>
    /// Whether the converter for <paramref name="type"/> reads "NaN", "Infinity" and "-Infinity"
    /// from a JSON string: only IEEE floating-point converters do, under either
    /// <see cref="JsonNumberHandling.AllowReadingFromString"/> or
    /// <see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/>.
    /// </summary>
    public static bool AllowsNamedFloatingPointLiterals(JsonNumberHandling numberHandling, Type type)
        => IsIeeeFloatingPoint(type)
            && (numberHandling & (JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) != 0;

    /// <summary>
    /// Whether the string token under <paramref name="reader"/> is one of the named literals that
    /// floating-point converters accept, which integer converters reject even when they read
    /// numbers from strings.
    /// </summary>
    public static bool IsNamedFloatingPointLiteral(ref Utf8JsonReader reader)
        => reader.ValueTextEquals("NaN"u8) || reader.ValueTextEquals("Infinity"u8) || reader.ValueTextEquals("-Infinity"u8);

    private static bool IsIeeeFloatingPoint(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(float) || type == typeof(double) || type == typeof(Half)
#if NET11_0_OR_GREATER
            || type == typeof(System.Numerics.BFloat16)
            || type == typeof(System.Numerics.Decimal32)
            || type == typeof(System.Numerics.Decimal64)
            || type == typeof(System.Numerics.Decimal128)
#endif
            ;
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
    /// source, used to disambiguate collection migrators by their first element. The resolved
    /// contract is used so derived collections such as <c>class IntCollection : List&lt;int&gt;</c>
    /// report their real element type.
    /// </summary>
    public static Type GetValueType(JsonTypeInfo collectionTypeInfo)
        => collectionTypeInfo.ElementType ?? typeof(object);
}
