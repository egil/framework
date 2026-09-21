#if NET11_0_OR_GREATER
using System.Numerics;
using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public partial class NumericSourceMigrationTests
{
    [Fact]
    public void Migrate_from_decimal64_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<Decimal64>>("41", options);

        Assert.NotNull(result);
        Assert.Equal((Decimal64)41, result.Value);
    }

    [Fact]
    public void Migrate_from_decimal32_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<Decimal32>>("1.5", options);

        Assert.NotNull(result);
        Assert.Equal((Decimal32)1.5m, result.Value);
    }

    [Fact]
    public void Migrate_from_decimal128_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<Decimal128>>("12345678901234567890.5", options);

        Assert.NotNull(result);
        Assert.Equal(Decimal128.Parse("12345678901234567890.5", System.Globalization.CultureInfo.InvariantCulture), result.Value);
    }

    [Fact]
    public void Migrate_from_bfloat16_to_custom_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<NumericState<BFloat16>>("0.5", options);

        Assert.NotNull(result);
        Assert.Equal((BFloat16)0.5f, result.Value);
    }

    [Fact]
    public void Migrate_from_list_of_decimal64_disambiguated_from_list_of_strings()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<Decimal64OrStringListState>("[1,2]", options);

        Assert.NotNull(result);
        Assert.Equal("from-decimal64-list", result.Source);
    }

    [Fact]
    public void Migrate_from_dictionary_of_decimal128_disambiguated_from_dictionary_of_strings()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<Decimal128OrStringDictionaryState>("""{"a":1.25}""", options);

        Assert.NotNull(result);
        Assert.Equal("from-decimal128-dictionary", result.Source);
    }

    [Fact]
    public void Int_source_wins_over_decimal64_source_for_a_number_as_in_1x()
    {
        // Decimal64 is a numeric source only since 2.0; the int source 1.x selected keeps the payload.
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<IntOrDecimal64State>("42", options);

        Assert.Equal("from-int", result!.Source);
    }

    [JsonMigratable]
    public record class Decimal64OrStringListState(string Source)
        : IMigrateFrom<List<Decimal64>, Decimal64OrStringListState>,
          IMigrateFrom<List<string>, Decimal64OrStringListState>
    {
        public static bool TryMigrateFrom(List<Decimal64> source, out Decimal64OrStringListState result)
        {
            result = new Decimal64OrStringListState("from-decimal64-list");
            return true;
        }

        public static bool TryMigrateFrom(List<string> source, out Decimal64OrStringListState result)
        {
            result = new Decimal64OrStringListState("from-string-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class Decimal128OrStringDictionaryState(string Source)
        : IMigrateFrom<Dictionary<string, Decimal128>, Decimal128OrStringDictionaryState>,
          IMigrateFrom<Dictionary<string, string>, Decimal128OrStringDictionaryState>
    {
        public static bool TryMigrateFrom(Dictionary<string, Decimal128> source, out Decimal128OrStringDictionaryState result)
        {
            result = new Decimal128OrStringDictionaryState("from-decimal128-dictionary");
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, string> source, out Decimal128OrStringDictionaryState result)
        {
            result = new Decimal128OrStringDictionaryState("from-string-dictionary");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrDecimal64State(string Source)
        : IMigrateFrom<int, IntOrDecimal64State>,
          IMigrateFrom<Decimal64, IntOrDecimal64State>
    {
        public static bool TryMigrateFrom(int source, out IntOrDecimal64State result)
        {
            result = new IntOrDecimal64State("from-int");
            return true;
        }

        public static bool TryMigrateFrom(Decimal64 source, out IntOrDecimal64State result)
        {
            result = new IntOrDecimal64State("from-decimal64");
            return true;
        }
    }
}
#endif
