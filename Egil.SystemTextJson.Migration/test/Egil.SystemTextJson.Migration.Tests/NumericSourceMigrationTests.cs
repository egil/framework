using System.Text.Json;

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
