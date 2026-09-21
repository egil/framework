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
    public void Named_floating_point_literal_does_not_reach_integer_source()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        options.AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NumericState<int>>("\"NaN\"", options));
    }

    [Fact]
    public void Named_literal_reaches_only_the_floating_point_source_when_both_flags_are_set()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals };
        options.AddJsonMigrationSupport();

        var named = JsonSerializer.Deserialize<IntOrDoubleState>("\"NaN\"", options);
        var quoted = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IntOrDoubleState>("\"42\"", options));

        Assert.Equal("from-double", named!.Source);
        Assert.Contains("ambiguous", quoted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enum_source_does_not_take_quoted_numbers()
    {
        // STJ's enum converter reads strings only as names regardless of number handling, so the
        // int source takes "42" without ambiguity.
        var options = CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PlainColour>("\"42\"", options));
        var result = JsonSerializer.Deserialize<IntOrPlainColourState>("\"42\"", options);

        Assert.Equal("from-int", result!.Source);
    }

    [Fact]
    public void Nullable_source_inherits_converter_override_of_its_underlying_type()
    {
        // A JsonConverter<int> that reads strings is wrapped in the nullable converter for int?,
        // so the int? source must not be shape-matched as a number.
        var options = CreateOptions();
        options.Converters.Add(new StringIntConverter());

        var result = JsonSerializer.Deserialize<NullableIntOrLongState>("42", options);

        Assert.Equal("from-long", result!.Source);
    }

    [Fact]
    public void Migratable_element_with_primitive_source_competes_for_primitive_elements()
    {
        // MigratableInt migrates from int, so [1] is valid for both lists; with two candidates
        // the payload is ambiguous, alone the migratable list is selected.
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MigratableIntListOrIntListState>("[1]", options));
        var alone = JsonSerializer.Deserialize<MigratableIntListState>("[1]", options);

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, alone!.Items[0].Value);
    }

    [Fact]
    public void Element_discriminator_wins_over_the_any_shape_element_guard()
    {
        // List<MigratableInt> may accept any element, but a discriminated first element still
        // identifies the tagged list exactly.
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<MigratableIntListOrTaggedListState>("""[{"$type":"tagged","name":"x"}]""", options);

        Assert.Equal("from-tagged-list", result!.Source);
    }

    [Fact]
    public void Migrate_from_base64_memory_string_to_custom_type()
    {
        var options = CreateOptions();

        var memory = JsonSerializer.Deserialize<NumericState<Memory<byte>>>("\"AQID\"", options);
        var readOnly = JsonSerializer.Deserialize<NumericState<ReadOnlyMemory<byte>>>("\"AQID\"", options);

        Assert.Equal([1, 2, 3], memory!.Value.ToArray());
        Assert.Equal([1, 2, 3], readOnly!.Value.ToArray());
    }

    [Fact]
    public void Object_element_collection_competes_for_every_element_shape()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ObjectListOrIntListState>("[1]", options));
        var alone = JsonSerializer.Deserialize<ObjectListState>("[1]", options);

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, alone!.Count);
    }

    [Fact]
    public void Overridden_migratable_element_has_no_discriminator_route()
    {
        // A resolver placed ahead of migration serves Tagged with a custom converter, so its
        // discriminator must not select the collection; with two candidates the payload is
        // ambiguous instead. Migration sits at the front of the chain, so a resolver that
        // overrides a [JsonMigratable] type has to be inserted before it.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain.Insert(0, new CustomTaggedResolver());
        options.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TaggedListOrItemListState>("""[{"$type":"tagged","name":"x"}]""", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Collection_with_overridden_element_converter_makes_element_disambiguation_ambiguous()
    {
        // The overridden element converter may accept any element, so neither list can be
        // chosen by element shape.
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ColourListOrItemListState>("""[{"name":"x"}]""", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Collection_with_overridden_element_converter_alone_is_still_selected()
    {
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var result = JsonSerializer.Deserialize<ColourListState>("""["Green"]""", options);

        Assert.Equal(Colour.Green, result!.Colours[0]);
    }

    [Fact]
    public void Source_with_resolver_supplied_converter_is_not_shape_matched()
    {
        // The resolver attaches a string-reading enum converter without touching options.Converters
        // or the type, so only the resolved contract reveals the override.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new StringColourResolver(),
        };
        options.AddJsonMigrationSupport();

        var result = JsonSerializer.Deserialize<IntOrColourState>("42", options);

        Assert.Equal("from-int", result!.Source);
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
    public void Derived_collection_source_is_disambiguated_by_its_real_element_type()
    {
        var options = CreateOptions();

        var numbers = JsonSerializer.Deserialize<IntCollectionOrItemListState>("[1,2]", options);
        var items = JsonSerializer.Deserialize<IntCollectionOrItemListState>("""[{"name":"x"}]""", options);

        Assert.Equal("from-int-collection", numbers!.Source);
        Assert.Equal("from-item-list", items!.Source);
    }

    [Fact]
    public void Nested_derived_collection_elements_are_matched_as_arrays()
    {
        var options = CreateOptions();

        var nested = JsonSerializer.Deserialize<IntCollectionListOrItemListState>("[[1,2]]", options);
        var items = JsonSerializer.Deserialize<IntCollectionListOrItemListState>("""[{"name":"x"}]""", options);

        Assert.Equal("from-int-collection-list", nested!.Source);
        Assert.Equal("from-item-list", items!.Source);
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

    public sealed class ColourNameConverter : JsonConverter<Colour>
    {
        public override Colour Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Enum.Parse<Colour>(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Colour value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    public sealed class StringColourResolver : System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
    {
        private readonly System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver inner = new();

        public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != typeof(Colour))
            {
                return inner.GetTypeInfo(type, options);
            }

            return System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<Colour>(options, new ColourNameConverter());
        }
    }

    [JsonMigratable]
    public record class ColourListState(List<Colour> Colours) : IMigrateFrom<List<Colour>, ColourListState>
    {
        public static bool TryMigrateFrom(List<Colour> source, out ColourListState result)
        {
            result = new ColourListState(source);
            return true;
        }
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

    public enum PlainColour
    {
        Red,
        Green,
    }

    public sealed class StringIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => int.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [JsonMigratable]
    public record class NullableIntOrLongState(string Source)
        : IMigrateFrom<int?, NullableIntOrLongState>,
          IMigrateFrom<long, NullableIntOrLongState>
    {
        public static bool TryMigrateFrom(int? source, out NullableIntOrLongState result)
        {
            result = new NullableIntOrLongState("from-nullable-int");
            return true;
        }

        public static bool TryMigrateFrom(long source, out NullableIntOrLongState result)
        {
            result = new NullableIntOrLongState("from-long");
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "migratable-int")]
    public record class MigratableInt(int Value) : IMigrateFrom<int, MigratableInt>
    {
        public static bool TryMigrateFrom(int source, out MigratableInt result)
        {
            result = new MigratableInt(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class MigratableIntListOrIntListState(string Source)
        : IMigrateFrom<List<MigratableInt>, MigratableIntListOrIntListState>,
          IMigrateFrom<List<int>, MigratableIntListOrIntListState>
    {
        public static bool TryMigrateFrom(List<MigratableInt> source, out MigratableIntListOrIntListState result)
        {
            result = new MigratableIntListOrIntListState("from-migratable-int-list");
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out MigratableIntListOrIntListState result)
        {
            result = new MigratableIntListOrIntListState("from-int-list");
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "tagged")]
    public record class Tagged(string Name);

    public sealed class TaggedNameConverter : JsonConverter<Tagged>
    {
        public override Tagged Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return new Tagged("custom");
        }

        public override void Write(Utf8JsonWriter writer, Tagged value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    public sealed class CustomTaggedResolver : System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
    {
        public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            => type == typeof(Tagged)
                ? System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<Tagged>(options, new TaggedNameConverter())
                : null;
    }

    [JsonMigratable]
    public record class TaggedListOrItemListState(string Source)
        : IMigrateFrom<List<Tagged>, TaggedListOrItemListState>,
          IMigrateFrom<List<Item>, TaggedListOrItemListState>
    {
        public static bool TryMigrateFrom(List<Tagged> source, out TaggedListOrItemListState result)
        {
            result = new TaggedListOrItemListState("from-tagged-list");
            return true;
        }

        public static bool TryMigrateFrom(List<Item> source, out TaggedListOrItemListState result)
        {
            result = new TaggedListOrItemListState("from-item-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class ObjectListOrIntListState(string Source)
        : IMigrateFrom<List<object>, ObjectListOrIntListState>,
          IMigrateFrom<List<int>, ObjectListOrIntListState>
    {
        public static bool TryMigrateFrom(List<object> source, out ObjectListOrIntListState result)
        {
            result = new ObjectListOrIntListState("from-object-list");
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out ObjectListOrIntListState result)
        {
            result = new ObjectListOrIntListState("from-int-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class ObjectListState(int Count) : IMigrateFrom<List<object>, ObjectListState>
    {
        public static bool TryMigrateFrom(List<object> source, out ObjectListState result)
        {
            result = new ObjectListState(source.Count);
            return true;
        }
    }

    [JsonMigratable]
    public record class MigratableIntListOrTaggedListState(string Source)
        : IMigrateFrom<List<MigratableInt>, MigratableIntListOrTaggedListState>,
          IMigrateFrom<List<Tagged>, MigratableIntListOrTaggedListState>
    {
        public static bool TryMigrateFrom(List<MigratableInt> source, out MigratableIntListOrTaggedListState result)
        {
            result = new MigratableIntListOrTaggedListState("from-migratable-int-list");
            return true;
        }

        public static bool TryMigrateFrom(List<Tagged> source, out MigratableIntListOrTaggedListState result)
        {
            result = new MigratableIntListOrTaggedListState("from-tagged-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class MigratableIntListState(List<MigratableInt> Items) : IMigrateFrom<List<MigratableInt>, MigratableIntListState>
    {
        public static bool TryMigrateFrom(List<MigratableInt> source, out MigratableIntListState result)
        {
            result = new MigratableIntListState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrPlainColourState(string Source)
        : IMigrateFrom<int, IntOrPlainColourState>,
          IMigrateFrom<PlainColour, IntOrPlainColourState>
    {
        public static bool TryMigrateFrom(int source, out IntOrPlainColourState result)
        {
            result = new IntOrPlainColourState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(PlainColour source, out IntOrPlainColourState result)
        {
            result = new IntOrPlainColourState("from-colour");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntOrDoubleState(string Source)
        : IMigrateFrom<int, IntOrDoubleState>,
          IMigrateFrom<double, IntOrDoubleState>
    {
        public static bool TryMigrateFrom(int source, out IntOrDoubleState result)
        {
            result = new IntOrDoubleState("from-int");
            return true;
        }

        public static bool TryMigrateFrom(double source, out IntOrDoubleState result)
        {
            result = new IntOrDoubleState("from-double");
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
    public record class IntCollectionListOrItemListState(string Source)
        : IMigrateFrom<List<IntCollection>, IntCollectionListOrItemListState>,
          IMigrateFrom<List<Item>, IntCollectionListOrItemListState>
    {
        public static bool TryMigrateFrom(List<IntCollection> source, out IntCollectionListOrItemListState result)
        {
            result = new IntCollectionListOrItemListState("from-int-collection-list");
            return true;
        }

        public static bool TryMigrateFrom(List<Item> source, out IntCollectionListOrItemListState result)
        {
            result = new IntCollectionListOrItemListState("from-item-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class IntCollectionOrItemListState(string Source)
        : IMigrateFrom<IntCollection, IntCollectionOrItemListState>,
          IMigrateFrom<List<Item>, IntCollectionOrItemListState>
    {
        public static bool TryMigrateFrom(IntCollection source, out IntCollectionOrItemListState result)
        {
            result = new IntCollectionOrItemListState("from-int-collection");
            return true;
        }

        public static bool TryMigrateFrom(List<Item> source, out IntCollectionOrItemListState result)
        {
            result = new IntCollectionOrItemListState("from-item-list");
            return true;
        }
    }

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
