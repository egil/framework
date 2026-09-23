using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// Which collection source a JSON array selects when several are registered: by element number
/// handling from the options or the collection's contract, and by the element's own migration history.
/// </summary>
public class CollectionSourceSelectionTests
{
    [Theory]
    [InlineData("[\"42\"]", 42f)]
    [InlineData("[\"NaN\"]", float.NaN)]
    public void Quoted_half_elements_select_numeric_collection_over_boolean_collection(string json, float expected)
    {
        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        }.AddJsonMigrationSupport();

        var result = JsonSerializer.Deserialize<HalfOrBooleanList>(json, options);

        Assert.NotNull(result);
        Assert.Equal("half", result.Source);
        Assert.Equal((Half)expected, result.Value);
    }

    [Theory]
    [InlineData("[\"42\"]", 42f)]
    [InlineData("[\"NaN\"]", float.NaN)]
    public void Collection_contract_number_handling_selects_numeric_elements_with_strict_root_options(string json, float expected)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(List<Half>))
            {
                info.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals;
            }
        });
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver }.AddJsonMigrationSupport();

        var result = JsonSerializer.Deserialize<HalfOrBooleanList>(json, options);

        Assert.NotNull(result);
        Assert.Equal("half", result.Source);
        Assert.Equal((Half)expected, result.Value);
    }

    [Fact]
    public void Object_elements_are_rejected_when_only_numeric_and_boolean_collections_are_registered()
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HalfOrBooleanList>("[{}]", options));
    }

    [Theory]
    [InlineData("[{\"$type\":\"element\",\"Value\":42}]")]
    [InlineData("[{}]")]
    public void Same_element_type_in_array_and_list_cannot_select_a_migration_by_registration_order(string json)
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AmbiguousElementCollections>(json, options));
    }

    public static TheoryData<string, string> ObjectElementPayloads => new()
    {
        { """[{"$type":"element","Value":42}]""", "annotated" },
        { $$"""[{"$type":"{{typeof(PlainElement).FullName}}","Value":42}]""", "plain" },
        { """[{"Value":42}]""", "dictionary" },
    };

    [Theory]
    [MemberData(nameof(ObjectElementPayloads))]
    public void Object_only_element_histories_select_object_collection_over_numeric_collection(string json, string expectedSource)
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();

        var result = JsonSerializer.Deserialize<ObjectHistoryCollection>(json, options);

        Assert.NotNull(result);
        Assert.Equal(expectedSource, result.Source);
        Assert.Equal(42, result.Value);
    }

    [JsonMigratable]
    public sealed record HalfOrBooleanList(string Source, Half Value) : IMigrateFrom<List<Half>, HalfOrBooleanList>, IMigrateFrom<List<bool>, HalfOrBooleanList>
    {
        public static bool TryMigrateFrom(List<Half> source, out HalfOrBooleanList result)
        {
            result = new("half", source[0]);
            return true;
        }

        public static bool TryMigrateFrom(List<bool> source, out HalfOrBooleanList result)
            => throw new InvalidOperationException("Numeric input must not select Boolean migration.");
    }

    [JsonMigratable(TypeDiscriminator = "element")]
    public sealed record Element(int Value);

    public sealed record PlainElement(int Value);

    [JsonMigratable]
    public sealed record ElementWithObjectHistory(string Source, int Value)
        : IMigrateFrom<Element, ElementWithObjectHistory>,
          IMigrateFrom<PlainElement, ElementWithObjectHistory>,
          IMigrateFrom<Dictionary<string, int>, ElementWithObjectHistory>
    {
        public static bool TryMigrateFrom(Element source, out ElementWithObjectHistory result)
        {
            result = new("annotated", source.Value);
            return true;
        }

        public static bool TryMigrateFrom(PlainElement source, out ElementWithObjectHistory result)
        {
            result = new("plain", source.Value);
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, int> source, out ElementWithObjectHistory result)
        {
            result = new("dictionary", source["Value"]);
            return true;
        }
    }

    [JsonMigratable]
    public sealed record ObjectHistoryCollection(string Source, int Value)
        : IMigrateFrom<List<ElementWithObjectHistory>, ObjectHistoryCollection>,
          IMigrateFrom<List<int>, ObjectHistoryCollection>
    {
        public static bool TryMigrateFrom(List<ElementWithObjectHistory> source, out ObjectHistoryCollection result)
        {
            result = new(source[0].Source, source[0].Value);
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out ObjectHistoryCollection result)
            => throw new InvalidOperationException("Object input must not select numeric collection migration.");
    }

    [JsonMigratable]
    public sealed record AmbiguousElementCollections : IMigrateFrom<Element[], AmbiguousElementCollections>, IMigrateFrom<List<Element>, AmbiguousElementCollections>
    {
        public static bool TryMigrateFrom(Element[] source, out AmbiguousElementCollections result)
            => throw new InvalidOperationException("Ambiguous input must not select array migration.");

        public static bool TryMigrateFrom(List<Element> source, out AmbiguousElementCollections result)
            => throw new InvalidOperationException("Ambiguous input must not select list migration.");
    }
}
