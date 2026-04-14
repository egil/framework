using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

public class NonObjectPayloadMigrationTests
{
    [Fact]
    public void Migrate_from_list_of_strings_to_custom_type()
    {
        var options = CreateOptions();
        var json = """["alpha","beta","gamma"]""";

        var result = JsonSerializer.Deserialize<StringListState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(["alpha", "beta", "gamma"], result.Values);
    }

    [Fact]
    public void Migrate_from_string_to_custom_type()
    {
        var options = CreateOptions();
        var json = """  "hello world"  """;

        var result = JsonSerializer.Deserialize<StringState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void Migrate_from_int_to_custom_type()
    {
        var options = CreateOptions();
        var json = "42";

        var result = JsonSerializer.Deserialize<IntState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Migrate_from_string_array_to_custom_type()
    {
        var options = CreateOptions();
        var json = """["x","y"]""";

        var result = JsonSerializer.Deserialize<StringArrayState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(["x", "y"], result.Items);
    }

    [Fact]
    public void Migrate_from_non_object_payload_with_mixed_migrators()
    {
        var options = CreateOptions();

        // MixedState has both object-based (OldMixedState) and collection-based (List<string>) migrators.
        // An array payload should match the collection migrator.
        var json = """["one","two"]""";

        var result = JsonSerializer.Deserialize<MixedState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(["one", "two"], result.Values);
        Assert.Equal("from-list", result.Source);
    }

    [Fact]
    public void Migrate_from_non_object_payload_with_mixed_migrators_object_source()
    {
        var options = CreateOptions();

        // With an object payload containing a discriminator, the object migrator should be used.
        var oldJson = JsonSerializer.Serialize(new OldMixedState("a,b"), options);

        var result = JsonSerializer.Deserialize<MixedState>(oldJson, options);

        Assert.NotNull(result);
        Assert.Equal(["a", "b"], result.Values);
        Assert.Equal("from-object", result.Source);
    }

    [Fact]
    public void Round_trip_after_non_object_migration()
    {
        var options = CreateOptions();

        // Migrate from array
        var arrayJson = """["alpha","beta"]""";
        var migrated = JsonSerializer.Deserialize<StringListState>(arrayJson, options);
        Assert.NotNull(migrated);

        // Serialize back — should produce object JSON with discriminator
        var serialized = JsonSerializer.Serialize(migrated, options);
        Assert.StartsWith("{\"$type\":", serialized, StringComparison.Ordinal);

        // Deserialize again — should take the happy path (no migration)
        var roundTripped = JsonSerializer.Deserialize<StringListState>(serialized, options);
        Assert.NotNull(roundTripped);
        Assert.Equal(migrated.Values, roundTripped.Values);
    }

    [Fact]
    public void Migration_tracking_set_for_non_object_migration()
    {
        var options = CreateOptions();
        var json = """["one","two"]""";

        var result = JsonSerializer.Deserialize<TrackedListState>(json, options);

        Assert.NotNull(result);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void Migration_tracking_false_after_round_trip()
    {
        var options = CreateOptions();

        // Migrate from array first
        var migrated = JsonSerializer.Deserialize<TrackedListState>("""["a"]""", options);
        Assert.NotNull(migrated);

        // Serialize and deserialize again — should be the happy path
        var json = JsonSerializer.Serialize(migrated, options);
        var roundTripped = JsonSerializer.Deserialize<TrackedListState>(json, options);

        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.MigratedDuringDeserialization);
    }

    [Fact]
    public void Non_object_payload_with_no_compatible_migrator_throws()
    {
        // NoArrayMigratorState only has an object-based migrator — no collection migrator.
        // An array payload should fall through to legacy handling and throw.
        var options = CreateOptions();

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<NoArrayMigratorState>("""["x"]""", options));
    }

    [Fact]
    public void External_migrator_for_non_object_payload()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder.RegisterMigrator<ExternalListMigrator>());
        var json = """["a","b","c"]""";

        var result = JsonSerializer.Deserialize<ExternalListTarget>(json, options);

        Assert.NotNull(result);
        Assert.Equal("a,b,c", result.Joined);
    }

    [Fact]
    public void Migrate_from_boolean_to_custom_type()
    {
        var options = CreateOptions();
        var json = "true";

        var result = JsonSerializer.Deserialize<BoolState>(json, options);

        Assert.NotNull(result);
        Assert.True(result.Enabled);
    }

    [Fact]
    public void Disambiguate_string_vs_int_primitive_migrators_string_payload()
    {
        var options = CreateOptions();
        var json = """ "hello" """;

        var result = JsonSerializer.Deserialize<MultiPrimitiveState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-string", result.Source);
        Assert.Equal("hello", result.StringValue);
    }

    [Fact]
    public void Disambiguate_string_vs_int_primitive_migrators_int_payload()
    {
        var options = CreateOptions();
        var json = "42";

        var result = JsonSerializer.Deserialize<MultiPrimitiveState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-int", result.Source);
        Assert.Equal(42, result.IntValue);
    }

    [Fact]
    public void Disambiguate_bool_vs_string_vs_int_primitive_migrators_bool_payload()
    {
        var options = CreateOptions();
        var json = "true";

        var result = JsonSerializer.Deserialize<MultiPrimitiveState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-bool", result.Source);
    }

    [Fact]
    public void Disambiguate_enumerable_migrators_by_first_element_string_array()
    {
        var options = CreateOptions();
        var json = """["a","b"]""";

        var result = JsonSerializer.Deserialize<MultiEnumerableState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-list-string", result.Source);
        Assert.Equal(["a", "b"], result.StringValues);
    }

    [Fact]
    public void Disambiguate_enumerable_migrators_by_first_element_int_array()
    {
        var options = CreateOptions();
        var json = "[1,2,3]";

        var result = JsonSerializer.Deserialize<MultiEnumerableState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-list-int", result.Source);
        Assert.Equal([1, 2, 3], result.IntValues);
    }

    [Fact]
    public void Disambiguate_enumerable_migrators_by_first_element_object_array()
    {
        var options = CreateOptions();
        var json = """[{"name":"x"},{"name":"y"}]""";

        var result = JsonSerializer.Deserialize<MultiEnumerableState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-list-item", result.Source);
    }

    [Fact]
    public void Ambiguous_enumerable_migrators_empty_array_throws()
    {
        var options = CreateOptions();
        var json = "[]";

        // Empty array can't be disambiguated by element type — throws.
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<MultiEnumerableState>(json, options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disambiguate_dictionary_migrators_by_first_value_string()
    {
        var options = CreateOptions();
        var json = """{"a":"hello"}""";

        var result = JsonSerializer.Deserialize<MultiDictState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-dict-string", result.Source);
    }

    [Fact]
    public void Disambiguate_dictionary_migrators_by_first_value_int()
    {
        var options = CreateOptions();
        var json = """{"a":42}""";

        var result = JsonSerializer.Deserialize<MultiDictState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-dict-int", result.Source);
    }

    [Fact]
    public void Disambiguate_dictionary_migrators_empty_object_treated_as_legacy()
    {
        var options = CreateOptions();
        var json = "{}";

        // Empty object hits the EndObject early return in Inspect,
        // treated as legacy before the dictionary fallback is reached.
        var result = JsonSerializer.Deserialize<MultiDictState>(json, options);

        Assert.NotNull(result);
    }

    [Fact]
    public void Disambiguate_enumerable_with_migratable_object_elements()
    {
        var options = CreateOptions();
        // Array of migratable objects — the discriminator identifies which migrator to use.
        var json = """[{"$type":"elem-v1","data":"x"},{"$type":"elem-v1","data":"y"}]""";

        var result = JsonSerializer.Deserialize<MigratableElementState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-elem-v1", result.Source);
    }

    [Fact]
    public void Disambiguate_enumerable_with_nested_array_elements()
    {
        var options = CreateOptions();
        var json = """[[1,2],[3,4]]""";

        var result = JsonSerializer.Deserialize<NestedArrayState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Values.Count);
    }

    [Fact]
    public void Disambiguate_dictionary_with_migratable_object_values()
    {
        var options = CreateOptions();
        var json = """{"a":{"$type":"elem-v1","data":"x"}}""";

        var result = JsonSerializer.Deserialize<MigratableDictValueState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-dict-elem-v1", result.Source);
    }

    [Fact]
    public void Disambiguate_enumerable_with_two_migratable_element_types_by_discriminator()
    {
        var options = CreateOptions();
        // Array of ElemV1 objects — discriminator identifies the element type.
        var json = """[{"$type":"elem-v1","data":"x"}]""";

        var result = JsonSerializer.Deserialize<TwoMigratableElementState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-elem-v1", result.Source);
    }

    [Fact]
    public void Disambiguate_enumerable_with_two_migratable_element_types_by_discriminator_v2()
    {
        var options = CreateOptions();
        var json = """[{"$type":"elem-v2","data":"y"}]""";

        var result = JsonSerializer.Deserialize<TwoMigratableElementState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-elem-v2", result.Source);
    }

    [Fact]
    public void Disambiguate_dictionary_with_two_migratable_value_types_by_discriminator()
    {
        var options = CreateOptions();
        var json = """{"a":{"$type":"elem-v2","data":"y"}}""";

        var result = JsonSerializer.Deserialize<TwoMigratableDictValueState>(json, options);

        Assert.NotNull(result);
        Assert.Equal("from-dict-elem-v2", result.Source);
    }

    [Fact]
    public void Migrate_from_dictionary_to_custom_type()
    {
        var options = CreateOptions();
        var json = """{"key1":"value1","key2":"value2"}""";

        var result = JsonSerializer.Deserialize<DictState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal("value1", result.Values["key1"]);
        Assert.Equal("value2", result.Values["key2"]);
    }

    [Fact]
    public void Migrate_from_dictionary_with_object_values_to_custom_type()
    {
        var options = CreateOptions();
        var json = """{"item1":{"name":"alpha"},"item2":{"name":"beta"}}""";

        var result = JsonSerializer.Deserialize<DictObjectState>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("alpha", result.Items["item1"].Name);
        Assert.Equal("beta", result.Items["item2"].Name);
    }

    [Fact]
    public void Round_trip_after_dictionary_migration()
    {
        var options = CreateOptions();
        var json = """{"a":"1","b":"2"}""";

        var migrated = JsonSerializer.Deserialize<DictState>(json, options);
        Assert.NotNull(migrated);

        // Serialize back — should produce object JSON with discriminator
        var serialized = JsonSerializer.Serialize(migrated, options);
        Assert.StartsWith("{\"$type\":", serialized, StringComparison.Ordinal);

        // Deserialize again — happy path (no migration)
        var roundTripped = JsonSerializer.Deserialize<DictState>(serialized, options);
        Assert.NotNull(roundTripped);
        Assert.Equal(migrated.Values, roundTripped.Values);
    }

    [Fact]
    public void Dictionary_migration_tracking()
    {
        var options = CreateOptions();
        var json = """{"x":"y"}""";

        var result = JsonSerializer.Deserialize<TrackedDictState>(json, options);

        Assert.NotNull(result);
        Assert.True(result.MigratedDuringDeserialization);
    }

    [Fact]
    public void Mixed_migrators_with_dictionary_and_object_sources()
    {
        var options = CreateOptions();

        // Dictionary payload → matches the Dictionary<string,string> migrator
        var dictJson = """{"k1":"v1"}""";
        var fromDict = JsonSerializer.Deserialize<DictMixedState>(dictJson, options);
        Assert.NotNull(fromDict);
        Assert.Equal("from-dict", fromDict.Source);
        Assert.Equal("v1", fromDict.Values["k1"]);

        // Object payload with discriminator → matches the OldDictMixedState migrator
        var oldJson = JsonSerializer.Serialize(new OldDictMixedState("k1=v1"), options);
        var fromObj = JsonSerializer.Deserialize<DictMixedState>(oldJson, options);
        Assert.NotNull(fromObj);
        Assert.Equal("from-object", fromObj.Source);
    }

    [Fact]
    public void External_migrator_for_dictionary_payload()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder.RegisterMigrator<ExternalDictMigrator>());
        var json = """{"a":"1","b":"2"}""";

        var result = JsonSerializer.Deserialize<ExternalDictTarget>(json, options);

        Assert.NotNull(result);
        Assert.Equal("a=1,b=2", result.Serialized);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    // --- Test types ---

    [JsonMigratable]
    public record class StringListState(List<string> Values) : IMigrateFrom<List<string>, StringListState>
    {
        public static bool TryMigrateFrom(List<string> source, out StringListState result)
        {
            result = new StringListState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class StringState(string Value) : IMigrateFrom<string, StringState>
    {
        public static bool TryMigrateFrom(string source, out StringState result)
        {
            result = new StringState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class IntState(int Value) : IMigrateFrom<int, IntState>
    {
        public static bool TryMigrateFrom(int source, out IntState result)
        {
            result = new IntState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class StringArrayState(string[] Items) : IMigrateFrom<string[], StringArrayState>
    {
        public static bool TryMigrateFrom(string[] source, out StringArrayState result)
        {
            result = new StringArrayState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class BoolState(bool Enabled) : IMigrateFrom<bool, BoolState>
    {
        public static bool TryMigrateFrom(bool source, out BoolState result)
        {
            result = new BoolState(source);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "old-mixed")]
    public record class OldMixedState(string CsvValues);

    [JsonMigratable(TypeDiscriminator = "mixed-v2")]
    public record class MixedState(List<string> Values, string Source)
        : IMigrateFrom<List<string>, MixedState>,
          IMigrateFrom<OldMixedState, MixedState>
    {
        public static bool TryMigrateFrom(List<string> source, out MixedState result)
        {
            result = new MixedState(source, "from-list");
            return true;
        }

        public static bool TryMigrateFrom(OldMixedState source, out MixedState result)
        {
            result = new MixedState(
                [.. source.CsvValues.Split(',', StringSplitOptions.RemoveEmptyEntries)],
                "from-object");
            return true;
        }
    }

    [JsonMigratable]
    public record class TrackedListState(List<string> Values)
        : IJsonMigrationTracked, IMigrateFrom<List<string>, TrackedListState>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(List<string> source, out TrackedListState result)
        {
            result = new TrackedListState(source);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "old-no-array")]
    public record class OldNoArrayState(string Data);

    [JsonMigratable]
    public record class NoArrayMigratorState(string Data) : IMigrateFrom<OldNoArrayState, NoArrayMigratorState>
    {
        public static bool TryMigrateFrom(OldNoArrayState source, out NoArrayMigratorState result)
        {
            result = new NoArrayMigratorState(source.Data);
            return true;
        }
    }

    [JsonMigratable]
    public record class ExternalListTarget(string Joined);

    public class ExternalListMigrator : IMigrate<List<string>, ExternalListTarget>
    {
        public bool TryMigrateFrom(List<string> source, out ExternalListTarget result)
        {
            result = new ExternalListTarget(string.Join(",", source));
            return true;
        }
    }

    [JsonMigratable]
    public record class DictState(Dictionary<string, string> Values)
        : IMigrateFrom<Dictionary<string, string>, DictState>
    {
        public static bool TryMigrateFrom(Dictionary<string, string> source, out DictState result)
        {
            result = new DictState(source);
            return true;
        }
    }

    public record class DictItem(string Name);

    [JsonMigratable]
    public record class DictObjectState(Dictionary<string, DictItem> Items)
        : IMigrateFrom<Dictionary<string, DictItem>, DictObjectState>
    {
        public static bool TryMigrateFrom(Dictionary<string, DictItem> source, out DictObjectState result)
        {
            result = new DictObjectState(source);
            return true;
        }
    }

    [JsonMigratable]
    public record class TrackedDictState(Dictionary<string, string> Values)
        : IJsonMigrationTracked, IMigrateFrom<Dictionary<string, string>, TrackedDictState>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(Dictionary<string, string> source, out TrackedDictState result)
        {
            result = new TrackedDictState(source);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "old-dict-mixed")]
    public record class OldDictMixedState(string KeyValues);

    [JsonMigratable(TypeDiscriminator = "dict-mixed-v2")]
    public record class DictMixedState(Dictionary<string, string> Values, string Source)
        : IMigrateFrom<Dictionary<string, string>, DictMixedState>,
          IMigrateFrom<OldDictMixedState, DictMixedState>
    {
        public static bool TryMigrateFrom(Dictionary<string, string> source, out DictMixedState result)
        {
            result = new DictMixedState(source, "from-dict");
            return true;
        }

        public static bool TryMigrateFrom(OldDictMixedState source, out DictMixedState result)
        {
            var dict = source.KeyValues.Split(',')
                .Select(kv => kv.Split('='))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);
            result = new DictMixedState(dict, "from-object");
            return true;
        }
    }

    [JsonMigratable]
    public record class ExternalDictTarget(string Serialized);

    public class ExternalDictMigrator : IMigrate<Dictionary<string, string>, ExternalDictTarget>
    {
        public bool TryMigrateFrom(Dictionary<string, string> source, out ExternalDictTarget result)
        {
            result = new ExternalDictTarget(string.Join(",", source.Select(kv => $"{kv.Key}={kv.Value}")));
            return true;
        }
    }

    [JsonMigratable]
    public record class MultiPrimitiveState(string? StringValue, int? IntValue, string Source)
        : IMigrateFrom<string, MultiPrimitiveState>,
          IMigrateFrom<int, MultiPrimitiveState>,
          IMigrateFrom<bool, MultiPrimitiveState>
    {
        public static bool TryMigrateFrom(string source, out MultiPrimitiveState result)
        {
            result = new MultiPrimitiveState(source, null, "from-string");
            return true;
        }

        public static bool TryMigrateFrom(int source, out MultiPrimitiveState result)
        {
            result = new MultiPrimitiveState(null, source, "from-int");
            return true;
        }

        public static bool TryMigrateFrom(bool source, out MultiPrimitiveState result)
        {
            result = new MultiPrimitiveState(null, null, "from-bool");
            return true;
        }
    }

    public record class EnumerableItem(string Name);

    [JsonMigratable]
    public record class MultiEnumerableState(List<string>? StringValues, List<int>? IntValues, string Source)
        : IMigrateFrom<List<string>, MultiEnumerableState>,
          IMigrateFrom<List<int>, MultiEnumerableState>,
          IMigrateFrom<List<EnumerableItem>, MultiEnumerableState>
    {
        public static bool TryMigrateFrom(List<string> source, out MultiEnumerableState result)
        {
            result = new MultiEnumerableState(source, null, "from-list-string");
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out MultiEnumerableState result)
        {
            result = new MultiEnumerableState(null, source, "from-list-int");
            return true;
        }

        public static bool TryMigrateFrom(List<EnumerableItem> source, out MultiEnumerableState result)
        {
            result = new MultiEnumerableState(null, null, "from-list-item");
            return true;
        }
    }

    [JsonMigratable]
    public record class MultiDictState(Dictionary<string, string>? StringDict, string Source)
        : IMigrateFrom<Dictionary<string, string>, MultiDictState>,
          IMigrateFrom<Dictionary<string, int>, MultiDictState>
    {
        public static bool TryMigrateFrom(Dictionary<string, string> source, out MultiDictState result)
        {
            result = new MultiDictState(source, "from-dict-string");
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, int> source, out MultiDictState result)
        {
            result = new MultiDictState(null, "from-dict-int");
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "elem-v1")]
    public record class ElemV1(string Data);

    [JsonMigratable(TypeDiscriminator = "elem-v2")]
    public record class ElemV2(string Data);

    [JsonMigratable]
    public record class MigratableElementState(string Source)
        : IMigrateFrom<List<ElemV1>, MigratableElementState>,
          IMigrateFrom<List<string>, MigratableElementState>
    {
        public static bool TryMigrateFrom(List<ElemV1> source, out MigratableElementState result)
        {
            result = new MigratableElementState("from-elem-v1");
            return true;
        }

        public static bool TryMigrateFrom(List<string> source, out MigratableElementState result)
        {
            result = new MigratableElementState("from-string-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class NestedArrayState(List<List<int>> Values)
        : IMigrateFrom<List<List<int>>, NestedArrayState>,
          IMigrateFrom<List<string>, NestedArrayState>
    {
        public static bool TryMigrateFrom(List<List<int>> source, out NestedArrayState result)
        {
            result = new NestedArrayState(source);
            return true;
        }

        public static bool TryMigrateFrom(List<string> source, out NestedArrayState result)
        {
            result = new NestedArrayState([]);
            return true;
        }
    }

    [JsonMigratable]
    public record class MigratableDictValueState(string Source)
        : IMigrateFrom<Dictionary<string, ElemV1>, MigratableDictValueState>,
          IMigrateFrom<Dictionary<string, string>, MigratableDictValueState>
    {
        public static bool TryMigrateFrom(Dictionary<string, ElemV1> source, out MigratableDictValueState result)
        {
            result = new MigratableDictValueState("from-dict-elem-v1");
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, string> source, out MigratableDictValueState result)
        {
            result = new MigratableDictValueState("from-dict-string");
            return true;
        }
    }

    [JsonMigratable]
    public record class TwoMigratableElementState(string Source)
        : IMigrateFrom<List<ElemV1>, TwoMigratableElementState>,
          IMigrateFrom<List<ElemV2>, TwoMigratableElementState>
    {
        public static bool TryMigrateFrom(List<ElemV1> source, out TwoMigratableElementState result)
        {
            result = new TwoMigratableElementState("from-elem-v1");
            return true;
        }

        public static bool TryMigrateFrom(List<ElemV2> source, out TwoMigratableElementState result)
        {
            result = new TwoMigratableElementState("from-elem-v2");
            return true;
        }
    }

    [JsonMigratable]
    public record class TwoMigratableDictValueState(string Source)
        : IMigrateFrom<Dictionary<string, ElemV1>, TwoMigratableDictValueState>,
          IMigrateFrom<Dictionary<string, ElemV2>, TwoMigratableDictValueState>
    {
        public static bool TryMigrateFrom(Dictionary<string, ElemV1> source, out TwoMigratableDictValueState result)
        {
            result = new TwoMigratableDictValueState("from-dict-elem-v1");
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, ElemV2> source, out TwoMigratableDictValueState result)
        {
            result = new TwoMigratableDictValueState("from-dict-elem-v2");
            return true;
        }
    }
}
