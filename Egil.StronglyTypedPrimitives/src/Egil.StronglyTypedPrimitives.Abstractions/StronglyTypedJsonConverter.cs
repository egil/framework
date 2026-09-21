// System.Text.Json ships in-box from net8.0 up, which is also the lowest target that supports the
// static abstract interface members the converter relies on. netstandard2.0 deliberately has no
// System.Text.Json reference, so the converter does not exist there.
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// A <see cref="JsonConverter{T}"/> that serializes a strongly typed primitive as its wrapped
/// primitive value, both as a JSON value and as a dictionary key (property name).
/// </summary>
/// <remarks>
/// The generator declares this converter on every strongly typed primitive through
/// <see cref="JsonConverterAttribute"/>. Declare it yourself on the partial declaration when the
/// type is serialized through a <see cref="JsonSerializerContext"/>, because the System.Text.Json
/// source generator cannot see attributes emitted by other source generators.
/// </remarks>
/// <typeparam name="TSelf">The strongly typed primitive.</typeparam>
/// <typeparam name="TPrimitive">The wrapped primitive type.</typeparam>
public sealed class StronglyTypedJsonConverter<TSelf, TPrimitive> : JsonConverter<TSelf>
    where TSelf : struct, IStronglyTypedPrimitive<TSelf, TPrimitive>
{
    // Resolved once per closed generic type. The built-in converters from JsonMetadataServices are
    // trim/AOT safe and format property names culture invariantly (for example decimal keys as
    // "1.5" and DateTime keys as ISO 8601 with the kind preserved), which is what makes dictionary
    // keys round-trip across machines with different cultures. Values only fall back to them when
    // the options have no contract for the primitive (see ReadPrimitive).
    private static readonly JsonConverter<TPrimitive>? builtInConverter = BuiltInPrimitiveConverters.Find<TPrimitive>();

    // Cache for the value converter used when the options have no contract for the primitive.
    // Options are read-only by the time any converter runs, so their Converters list cannot change
    // afterwards and the resolution is stable per options instance; caching it also keeps a
    // JsonConverterFactory from creating a new converter for every value. The weak table lets the
    // options be collected as usual.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonConverter<TPrimitive>> valueConverters = new();

    public override TSelf Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Converters for value types receive JSON null tokens (HandleNull defaults to true for
        // structs). Mirror the serializer: null is the Empty instance for primitives that can
        // be null and an error for those that cannot, instead of letting the primitive converter
        // fail with a less descriptive reader exception.
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default(TPrimitive) is null
                ? TSelf.Empty
                : throw new JsonException($"The JSON value could not be converted to {typeof(TSelf)}.");
        }

        var rawValue = ReadPrimitive(ref reader, options);

        // An invalid value maps to Empty rather than throwing, as the generated TryParse and the
        // per-type converter generated before 2.0 do. Empty is the generated default instance
        // unless the type declares its own, which is why this is not simply default.
        return rawValue is not null && TSelf.IsValueValid(rawValue, throwIfInvalid: false)
            ? TSelf.Create(rawValue)
            : TSelf.Empty;
    }

    public override void Write(Utf8JsonWriter writer, TSelf value, JsonSerializerOptions options)
    {
        var rawValue = value.Value;

        // The serializer normally writes null itself before reaching a converter, so the primitive
        // converters do not expect a null argument.
        if (rawValue is null)
        {
            writer.WriteNullValue();
            return;
        }

        WritePrimitive(writer, rawValue, options);
    }

    public override TSelf ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rawValue = GetBuiltInPrimitiveConverter(options).ReadAsPropertyName(ref reader, typeof(TPrimitive), options);

        // A dictionary key that fails validation throws instead of mapping to the default instance,
        // because several invalid keys would otherwise collapse into duplicate default keys.
        TSelf.IsValueValid(rawValue, throwIfInvalid: true);
        return TSelf.Create(rawValue);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, [System.Diagnostics.CodeAnalysis.DisallowNull] TSelf value, JsonSerializerOptions options)
    {
        var rawValue = value.Value;

        // A default instance of a string-based primitive wraps null. The primitive converters reject
        // a null property name, so it is written as the empty name, which is also what the
        // per-type converter generated before 2.0 did (it used ToString(), which maps null to "").
        if (rawValue is null)
        {
            writer.WritePropertyName(string.Empty);
            return;
        }

        GetBuiltInPrimitiveConverter(options).WriteAsPropertyName(writer, rawValue, options);
    }

    // Values go through the options' contract for the primitive whenever the options have one, so
    // the options' JsonNumberHandling (for example AllowReadingFromString from
    // JsonSerializerDefaults.Web) and any custom converter registered for the primitive apply
    // exactly as they do to a plain primitive property. Calling the built-in converter directly
    // bypasses both: its Read never consults NumberHandling, so `"42"` was rejected for an
    // int-based primitive even though the OpenAPI schema advertises the string form. Without a
    // contract (a JsonSerializerContext without metadata for the primitive type) a converter
    // registered in options.Converters is used, then the built-in one. Property names
    // deliberately stay on the built-in converters so keys remain culture invariant regardless of
    // the options.
    private static TPrimitive? ReadPrimitive(ref Utf8JsonReader reader, JsonSerializerOptions options)
        => options.TryGetTypeInfo(typeof(TPrimitive), out var typeInfo)
            ? JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<TPrimitive>)typeInfo)
            : GetPrimitiveValueConverter(options).Read(ref reader, typeof(TPrimitive), options);

    private static void WritePrimitive(Utf8JsonWriter writer, TPrimitive rawValue, JsonSerializerOptions options)
    {
        if (options.TryGetTypeInfo(typeof(TPrimitive), out var typeInfo))
        {
            JsonSerializer.Serialize(writer, rawValue, (JsonTypeInfo<TPrimitive>)typeInfo);
            return;
        }

        GetPrimitiveValueConverter(options).Write(writer, rawValue, options);
    }

    private static JsonConverter<TPrimitive> GetPrimitiveValueConverter(JsonSerializerOptions options)
        => valueConverters.GetValue(options, static o => ResolvePrimitiveValueConverter(o));

    // Mirrors the serializer's own precedence for a plain primitive: a converter registered in
    // options.Converters wins over the built-in one, so a custom int or decimal converter still
    // applies to strongly typed values when the type info resolver has no metadata for the
    // primitive. A factory is unwrapped the way the serializer does it; one that declines to
    // create a converter is skipped.
    private static JsonConverter<TPrimitive> ResolvePrimitiveValueConverter(JsonSerializerOptions options)
    {
        foreach (var converter in options.Converters)
        {
            if (!converter.CanConvert(typeof(TPrimitive)))
            {
                continue;
            }

            var resolved = converter is JsonConverterFactory factory
                ? factory.CreateConverter(typeof(TPrimitive), options)
                : converter;
            if (resolved is JsonConverter<TPrimitive> primitiveConverter)
            {
                return primitiveConverter;
            }
        }

        return GetBuiltInPrimitiveConverter(options);
    }

    private static JsonConverter<TPrimitive> GetBuiltInPrimitiveConverter(JsonSerializerOptions options)
    {
        if (builtInConverter is not null)
        {
            return builtInConverter;
        }

        // Fallback for primitives without a built-in converter (for example a user-defined type).
        // Resolving through the options keeps this path free of RequiresUnreferencedCode, but it
        // requires the primitive type to be known to the options' type info resolver.
        return (JsonConverter<TPrimitive>)options.GetTypeInfo(typeof(TPrimitive)).Converter;
    }
}

/// <summary>
/// Maps primitive types to the built-in <see cref="JsonMetadataServices"/> converters so
/// <see cref="StronglyTypedJsonConverter{TSelf, TPrimitive}"/> can serialize them without
/// reflection or a type info resolver.
/// </summary>
internal static class BuiltInPrimitiveConverters
{
    private static readonly Dictionary<Type, JsonConverter> converters = new()
    {
        [typeof(bool)] = JsonMetadataServices.BooleanConverter,
        [typeof(byte)] = JsonMetadataServices.ByteConverter,
        [typeof(sbyte)] = JsonMetadataServices.SByteConverter,
        [typeof(char)] = JsonMetadataServices.CharConverter,
        [typeof(short)] = JsonMetadataServices.Int16Converter,
        [typeof(ushort)] = JsonMetadataServices.UInt16Converter,
        [typeof(int)] = JsonMetadataServices.Int32Converter,
        [typeof(uint)] = JsonMetadataServices.UInt32Converter,
        [typeof(long)] = JsonMetadataServices.Int64Converter,
        [typeof(ulong)] = JsonMetadataServices.UInt64Converter,
        [typeof(Int128)] = JsonMetadataServices.Int128Converter,
        [typeof(UInt128)] = JsonMetadataServices.UInt128Converter,
        [typeof(Half)] = JsonMetadataServices.HalfConverter,
        [typeof(float)] = JsonMetadataServices.SingleConverter,
        [typeof(double)] = JsonMetadataServices.DoubleConverter,
        [typeof(decimal)] = JsonMetadataServices.DecimalConverter,
        [typeof(string)] = JsonMetadataServices.StringConverter,
        [typeof(Guid)] = JsonMetadataServices.GuidConverter,
        [typeof(DateTime)] = JsonMetadataServices.DateTimeConverter,
        [typeof(DateTimeOffset)] = JsonMetadataServices.DateTimeOffsetConverter,
        [typeof(DateOnly)] = JsonMetadataServices.DateOnlyConverter,
        [typeof(TimeOnly)] = JsonMetadataServices.TimeOnlyConverter,
        [typeof(TimeSpan)] = JsonMetadataServices.TimeSpanConverter,
        [typeof(Uri)] = JsonMetadataServices.UriConverter,
        [typeof(Version)] = JsonMetadataServices.VersionConverter,
        [typeof(byte[])] = JsonMetadataServices.ByteArrayConverter,
    };

    public static JsonConverter<TPrimitive>? Find<TPrimitive>()
        => converters.TryGetValue(typeof(TPrimitive), out var converter)
            ? (JsonConverter<TPrimitive>)converter
            : null;
}
#endif