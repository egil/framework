using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// Non-object payloads are matched to a migrator by the CLR shape of its source type.
/// These tests pin that every numeric type System.Text.Json can read from a JSON number
/// token is treated as a numeric source, not only the types enumerated by <see cref="TypeCode"/>.
/// </summary>
public partial class NumericSourceMigrationTests
{
    [Fact]
    public void Migrate_from_long_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<long>>("9007199254740993", options);

        Assert.NotNull(result);
        Assert.Equal(9007199254740993L, result.Value);
    }

    [Fact]
    public void Migrate_from_double_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<double>>("1.5", options);

        Assert.NotNull(result);
        Assert.Equal(1.5d, result.Value);
    }

    [Fact]
    public void Migrate_from_decimal_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<decimal>>("12.34", options);

        Assert.NotNull(result);
        Assert.Equal(12.34m, result.Value);
    }

    [Fact]
    public void Migrate_from_half_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<Half>>("2.5", options);

        Assert.NotNull(result);
        Assert.Equal((Half)2.5f, result.Value);
    }

    [Fact]
    public void Migrate_from_int128_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<Int128>>("170141183460469231731687303715884105727", options);

        Assert.NotNull(result);
        Assert.Equal(Int128.MaxValue, result.Value);
    }

    [Fact]
    public void Migrate_from_uint128_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<UInt128>>("340282366920938463463374607431768211455", options);

        Assert.NotNull(result);
        Assert.Equal(UInt128.MaxValue, result.Value);
    }

    [Fact]
    public void Migrate_from_nullable_int_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<int?>>("7", options);

        Assert.NotNull(result);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Migrate_from_list_of_half_disambiguated_from_list_of_strings()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<HalfOrStringListState>("[1.5,2.5]", options);

        Assert.NotNull(result);
        Assert.Equal("from-half-list", result.Source);
    }

    [Fact]
    public void Ambiguous_numeric_migrators_throw_for_number_payload()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AmbiguousNumericState>("42", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quoted_number_reaches_numeric_source_when_reading_numbers_from_strings_is_allowed()
    {
        // JsonSerializerDefaults.Web enables AllowReadingFromString.
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<int>>("\"42\"", options);

        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Named_floating_point_literal_reaches_floating_point_source()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        options.AddJsonMigrationSupport();

        var result = JsonSerializer.Deserialize<NumericState<double>>("\"NaN\"", options);

        Assert.NotNull(result);
        Assert.True(double.IsNaN(result.Value));
    }

    [Fact]
    public void Quoted_number_is_not_treated_as_numeric_source_under_strict_number_handling()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NumericState<int>>("\"42\"", options));
    }

    [Fact]
    public void String_source_wins_quoted_numbers_over_numeric_source()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<IntOrStringState>("\"42\"", options);

        Assert.NotNull(result);
        Assert.Equal("from-string", result.Source);
    }

    [Fact]
    public void Quoted_number_elements_are_ambiguous_between_numeric_collection_sources()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IntOrLongListState>("[\"42\"]", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quoted_number_elements_reach_numeric_collection_source_without_string_alternative()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<IntOrItemListState>("[\"42\"]", options);

        Assert.NotNull(result);
        Assert.Equal("from-int-list", result.Source);
    }

    [Fact]
    public void BigInteger_source_is_not_a_numeric_shape()
    {
        // BigInteger implements INumberBase<T> but has no built-in STJ converter, so it must not
        // compete with int for number tokens.
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<IntOrBigIntegerState>("42", options);

        Assert.NotNull(result);
        Assert.Equal("from-int", result.Source);
    }

    [Fact]
    public void Migrate_from_base64_byte_array_string_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<byte[]>>("\"AQID\"", options);

        Assert.NotNull(result);
        Assert.Equal([1, 2, 3], result.Value);
    }

    [Fact]
    public void Byte_array_source_does_not_compete_for_array_payloads()
    {
        var options = CreateOptions();

        var fromArray = JsonSerializer.Deserialize<BytesOrIntListState>("[1,2]", options);
        var fromBase64 = JsonSerializer.Deserialize<BytesOrIntListState>("\"AQID\"", options);

        Assert.Equal("from-int-list", fromArray!.Source);
        Assert.Equal("from-bytes", fromBase64!.Source);
    }

    [Fact]
    public void Byte_array_elements_do_not_compete_for_nested_array_payloads()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<BytesOrNestedListState>("[[1,2]]", options);

        Assert.Equal("from-nested-list", result!.Source);
    }

    [Fact]
    public void Source_with_converter_override_is_not_shape_matched()
    {
        // JsonStringEnumConverter makes the enum read strings, which the CLR shape cannot show,
        // so the enum source is excluded and the int source takes the number without ambiguity.
        var options = CreateOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var result = JsonSerializer.Deserialize<IntOrColourState>("42", options);

        Assert.Equal("from-int", result!.Source);
    }

    [Fact]
    public void Collection_with_overridden_element_converter_is_not_shape_matched()
    {
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var result = JsonSerializer.Deserialize<ColourListOrItemListState>("""[{"name":"x"}]""", options);

        Assert.Equal("from-item-list", result!.Source);
    }

    [Fact]
    public void Migrate_from_guid_string_to_custom_type()
    {
        var options = CreateOptions();
        var id = Guid.NewGuid();

        var result = JsonSerializer.Deserialize<NumericState<Guid>>($"\"{id}\"", options);

        Assert.NotNull(result);
        Assert.Equal(id, result.Value);
    }

    [Fact]
    public void Migrate_from_non_generic_collection_source_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<IntCollectionState>("[1,2,3]", options);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    public record class Item(string Name);

    public enum Colour
    {
        Red,
        Green,
    }

    [JsonMigratable]
    public record class ColourListOrItemListState(string Source)
        : IMigrateFrom<List<Colour>, ColourListOrItemListState>,
          IMigrateFrom<List<Item>, ColourListOrItemListState>
    {
        public static bool TryMigrateFrom(List<Colour> source, out ColourListOrItemListState result)
        {
            result = new ColourListOrItemListState("from-colour-list");
            return true;
        }

        public static bool TryMigrateFrom(List<Item> source, out ColourListOrItemListState result)
        {
            result = new ColourListOrItemListState("from-item-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrColourState(string Source)
        : IMigrateFrom<int, IntOrColourState>,
          IMigrateFrom<Colour, IntOrColourState>
    {
        public static bool TryMigrateFrom(int source, out IntOrColourState result)
        {
            result = new IntOrColourState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(Colour source, out IntOrColourState result)
        {
            result = new IntOrColourState("from-colour");
            return true;
        }
    }

    [JsonMigratable]
    public record class BytesOrNestedListState(string Source)
        : IMigrateFrom<byte[][], BytesOrNestedListState>,
          IMigrateFrom<List<List<int>>, BytesOrNestedListState>
    {
        public static bool TryMigrateFrom(byte[][] source, out BytesOrNestedListState result)
        {
            result = new BytesOrNestedListState("from-bytes");
            return true;
        }

        public static bool TryMigrateFrom(List<List<int>> source, out BytesOrNestedListState result)
        {
            result = new BytesOrNestedListState("from-nested-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class BytesOrIntListState(string Source)
        : IMigrateFrom<byte[], BytesOrIntListState>,
          IMigrateFrom<List<int>, BytesOrIntListState>
    {
        public static bool TryMigrateFrom(byte[] source, out BytesOrIntListState result)
        {
            result = new BytesOrIntListState("from-bytes");
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out BytesOrIntListState result)
        {
            result = new BytesOrIntListState("from-int-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrLongListState(string Source)
        : IMigrateFrom<List<int>, IntOrLongListState>,
          IMigrateFrom<List<long>, IntOrLongListState>
    {
        public static bool TryMigrateFrom(List<int> source, out IntOrLongListState result)
        {
            result = new IntOrLongListState("from-int-list");
            return true;
        }

        public static bool TryMigrateFrom(List<long> source, out IntOrLongListState result)
        {
            result = new IntOrLongListState("from-long-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrItemListState(string Source)
        : IMigrateFrom<List<int>, IntOrItemListState>,
          IMigrateFrom<List<Item>, IntOrItemListState>
    {
        public static bool TryMigrateFrom(List<int> source, out IntOrItemListState result)
        {
            result = new IntOrItemListState("from-int-list");
            return true;
        }

        public static bool TryMigrateFrom(List<Item> source, out IntOrItemListState result)
        {
            result = new IntOrItemListState("from-item-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrStringState(string Source)
        : IMigrateFrom<int, IntOrStringState>,
          IMigrateFrom<string, IntOrStringState>
    {
        public static bool TryMigrateFrom(int source, out IntOrStringState result)
        {
            result = new IntOrStringState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(string source, out IntOrStringState result)
        {
            result = new IntOrStringState("from-string");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrBigIntegerState(string Source)
        : IMigrateFrom<int, IntOrBigIntegerState>,
          IMigrateFrom<System.Numerics.BigInteger, IntOrBigIntegerState>
    {
        public static bool TryMigrateFrom(int source, out IntOrBigIntegerState result)
        {
            result = new IntOrBigIntegerState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(System.Numerics.BigInteger source, out IntOrBigIntegerState result)
        {
            result = new IntOrBigIntegerState("from-biginteger");
            return true;
        }
    }

    public class IntCollection : List<int>;

    [JsonMigratable]
    public record class IntCollectionState(int Count) : IMigrateFrom<IntCollection, IntCollectionState>
    {
        public static bool TryMigrateFrom(IntCollection source, out IntCollectionState result)
        {
            result = new IntCollectionState(source.Count);
            return true;
        }
    }

    [JsonMigratable]
    public record class NumericState<TNumber>(TNumber Value) : IMigrateFrom<TNumber, NumericState<TNumber>>
    {
        public static bool TryMigrateFrom(TNumber source, out NumericState<TNumber> result)
        {
            result = new NumericState<TNumber>(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class HalfOrStringListState(string Source)
        : IMigrateFrom<List<Half>, HalfOrStringListState>,
          IMigrateFrom<List<string>, HalfOrStringListState>
    {
        public static bool TryMigrateFrom(List<Half> source, out HalfOrStringListState result)
        {
            result = new HalfOrStringListState("from-half-list");
            return true;
        }

        public static bool TryMigrateFrom(List<string> source, out HalfOrStringListState result)
        {
            result = new HalfOrStringListState("from-string-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class AmbiguousNumericState(string Source)
        : IMigrateFrom<int, AmbiguousNumericState>,
          IMigrateFrom<Half, AmbiguousNumericState>
    {
        public static bool TryMigrateFrom(int source, out AmbiguousNumericState result)
        {
            result = new AmbiguousNumericState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(Half source, out AmbiguousNumericState result)
        {
            result = new AmbiguousNumericState("from-half");
            return true;
        }
    }
}
