#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// On .NET 11 a C# union whose cases are <c>[JsonMigratable]</c> types is classified by the
/// migration discriminator, so old and current payloads route to the case that migrates them.
/// </summary>
public partial class UnionMigrationTests
{
    [Fact]
    public void Union_routes_current_payload_to_migratable_case()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v2","radius":2.5}""", options);

        var circle = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(2.5, circle.Radius);
    }

    [Fact]
    public void Union_routes_second_case_by_discriminator()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<Shape>("""{"$type":"rect-v2","width":1,"height":2}""", options);

        var rectangle = Assert.IsType<RectangleV2>(shape.Value);
        Assert.Equal(2, rectangle.Height);
    }

    [Fact]
    public void Union_migrates_old_payload_through_static_migrator()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v1","r":3}""", options);

        var circle = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(3, circle.Radius);
        Assert.True(circle.MigratedDuringDeserialization);
    }

    [Fact]
    public void Union_migrates_old_payload_through_external_migrator()
    {
        var options = CreateOptions(builder => builder.RegisterMigrator<RectangleV1, RectangleV2, RectangleMigrator>());

        var shape = JsonSerializer.Deserialize<Shape>("""{"$type":"rect-v1","w":4,"h":5}""", options);

        var rectangle = Assert.IsType<RectangleV2>(shape.Value);
        Assert.Equal(4, rectangle.Width);
        Assert.Equal(5, rectangle.Height);
    }

    [Fact]
    public void Union_round_trip_writes_case_discriminator()
    {
        var options = CreateOptions();
        Shape shape = new CircleV2(1.5);

        var json = JsonSerializer.Serialize(shape, options);
        var roundTripped = JsonSerializer.Deserialize<Shape>(json, options);

        Assert.Equal("""{"$type":"circle-v2","radius":1.5}""", json);
        Assert.IsType<CircleV2>(roundTripped.Value);
    }

    [Fact]
    public void Union_with_primitive_cases_routes_by_token_shape()
    {
        var options = CreateOptions();

        var text = JsonSerializer.Deserialize<ShapeOrScalar>("\"label\"", options);
        var number = JsonSerializer.Deserialize<ShapeOrScalar>("42", options);
        var flag = JsonSerializer.Deserialize<ShapeOrScalar>("true", options);
        var shape = JsonSerializer.Deserialize<ShapeOrScalar>("""{"$type":"circle-v1","r":1}""", options);

        Assert.Equal("label", text.Value);
        Assert.Equal(42, number.Value);
        Assert.Equal(true, flag.Value);
        Assert.IsType<CircleV2>(shape.Value);
    }

    [Fact]
    public void Union_routes_discriminator_less_object_to_case_with_undiscriminated_source()
    {
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<PointOrLabel>("""{"x":1,"y":2}""", options);

        var point = Assert.IsType<PointV2>(value.Value);
        Assert.Equal(1, point.X);
        Assert.Equal(2, point.Y);
        Assert.True(point.MigratedDuringDeserialization);
    }

    [Fact]
    public void Union_routes_discriminator_less_object_to_single_plain_object_case()
    {
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<ShapeOrNote>("""{"text":"hello"}""", options);

        var note = Assert.IsType<Note>(value.Value);
        Assert.Equal("hello", note.Text);
    }

    [Fact]
    public void Union_null_payload_yields_default_union()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<Shape>("null", options);

        Assert.Null(shape.Value);
    }

    [Fact]
    public void Union_nested_in_migratable_parent_and_collection_is_classified()
    {
        var options = CreateOptions();
        var json = """{"$type":"drawing","shapes":[{"$type":"circle-v1","r":1},{"$type":"rect-v2","width":2,"height":3}]}""";

        var drawing = JsonSerializer.Deserialize<Drawing>(json, options);

        Assert.NotNull(drawing);
        Assert.Collection(
            drawing.Shapes,
            first => Assert.IsType<CircleV2>(first.Value),
            second => Assert.IsType<RectangleV2>(second.Value));
    }

    [Fact]
    public void Union_unknown_discriminator_lists_known_values()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Shape>("""{"$type":"triangle","sides":3}""", options));

        Assert.Contains("triangle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rect-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_discriminator_less_object_without_fallback_case_throws()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Shape>("""{"radius":1,"$type":"circle-v2"}""", options));

        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_two_plain_object_cases_is_ambiguous_for_discriminator_less_object()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrTwoNotes>("""{"text":"hello"}""", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Union_with_explicit_structural_classifier_still_rejects_migratable_case()
    {
        var options = CreateOptions();

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<StructuralShapeOrNote>("""{"text":"hello"}""", options));
    }

    [Fact]
    public void Union_classification_honours_custom_discriminator_property_name()
    {
        var options = CreateOptions(builder => builder.SetTypeDiscriminatorPropertyName("kind"));

        var shape = JsonSerializer.Deserialize<Shape>("""{"kind":"circle-v1","r":7}""", options);

        var circle = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(7, circle.Radius);
    }

    [Fact]
    public void Union_classification_works_with_source_generated_context()
    {
        var options = new JsonSerializerOptions(ShapeJsonContext.Default.Options);
        options.AddJsonMigrationSupport();

        var shape = JsonSerializer.Deserialize<AnnotatedShape>("""{"$type":"circle-v1","r":9}""", options);

        var circle = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(9, circle.Radius);
    }

    [Fact]
    public void Union_annotated_with_classifier_attribute_works_with_reflection_options()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<AnnotatedShape>("""{"$type":"rect-v2","width":1,"height":2}""", options);

        Assert.IsType<RectangleV2>(shape.Value);
    }

    [Fact]
    public void Union_annotated_with_classifier_attribute_requires_migration_support()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<AnnotatedShape>("""{"$type":"rect-v2","width":1,"height":2}""", options));

        Assert.Contains("AddJsonMigrationSupport", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Polymorphic_base_with_migratable_derived_types_is_still_unsupported()
    {
        // Pins the documented limitation: STJ's polymorphic metadata protocol still requires
        // derived converters to support metadata, which custom converters cannot opt into.
        var options = CreateOptions();

        Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Deserialize<PolymorphicShape>("""{"$type":"poly-circle","radius":1}""", options));
    }

    [Fact]
    public void Union_routes_array_and_dictionary_cases_by_shape()
    {
        var options = CreateOptions();

        var list = JsonSerializer.Deserialize<ShapeOrCollections>("[1,2]", options);
        var dictionary = JsonSerializer.Deserialize<ShapeOrCollections>("""{"a":1}""", options);

        Assert.Equal([1, 2], Assert.IsType<List<int>>(list.Value));
        Assert.Equal(1, Assert.IsType<Dictionary<string, int>>(dictionary.Value)["a"]);
    }

    [Fact]
    public void Union_payload_shape_without_matching_case_throws()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Shape>("[1,2]", options));

        Assert.Contains("array", exception.Message, StringComparison.Ordinal);
        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_two_string_shaped_cases_is_ambiguous_for_string_payload()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrStringOrChar>("\"x\"", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Union_with_nested_union_case_refuses_shape_fallback_for_discriminator_less_object()
    {
        // The nested union could accept the object itself, so routing it elsewhere would
        // silently drop data; only discriminated payloads are classified.
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrNestedUnion>("""{"text":"hello"}""", options));

        Assert.Contains(nameof(ShapeOrNote), exception.Message, StringComparison.Ordinal);
        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_nested_union_case_refuses_shape_fallback_for_primitives()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrNestedUnion>("\"hello\"", options));

        Assert.Contains(nameof(ShapeOrNote), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_nested_union_case_still_routes_discriminated_payloads()
    {
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<ShapeOrNestedUnion>("""{"$type":"circle-v1","r":4}""", options);

        var circle = Assert.IsType<CircleV2>(value.Value);
        Assert.Equal(4, circle.Radius);
    }

    [Fact]
    public void Union_discriminator_that_is_not_a_string_throws()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Shape>("""{"$type":7}""", options));

        Assert.Contains("Expected discriminator string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_cases_claiming_the_same_discriminator_fails_at_configuration()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<DuplicateDiscriminatorUnion>("""{"$type":"circle-v2","radius":1}""", options));

        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_two_undiscriminated_source_cases_fails_at_configuration()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<TwoUndiscriminatedUnion>("""{"x":1,"y":2}""", options));

        Assert.Contains(nameof(JsonMigratableAttribute.UndiscriminatedSourceType), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classifier_declines_unions_without_migratable_cases()
    {
        var options = CreateOptions();
        options.MakeReadOnly(populateMissingResolver: true);

        var plainUnion = options.GetTypeInfo(typeof(PlainUnion));

        Assert.Null(plainUnion.TypeClassifier);
    }

    [Fact]
    public void Union_static_and_external_migrators_for_the_same_source_share_one_route()
    {
        var options = CreateOptions(builder => builder.RegisterMigrator<CircleV1, CircleV2, CircleMigrator>());

        var shape = JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v1","r":3}""", options);

        Assert.IsType<CircleV2>(shape.Value);
    }

    [Fact]
    public void Classifier_rejects_tokens_the_union_converter_never_forwards()
    {
        // The union converter handles JSON null before classifying, so a null token can only
        // reach the classifier when it is invoked directly.
        var options = CreateOptions();
        options.MakeReadOnly(populateMissingResolver: true);
        var classifier = options.GetTypeInfo(typeof(Shape)).TypeClassifier;
        Assert.NotNull(classifier);

        var exception = Assert.Throws<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("null"u8);
            reader.Read();
            classifier(ref reader);
        });

        Assert.Contains("Null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classifier_reports_truncated_object_when_invoked_directly()
    {
        var options = CreateOptions();
        options.MakeReadOnly(populateMissingResolver: true);
        var classifier = options.GetTypeInfo(typeof(Shape)).TypeClassifier;
        Assert.NotNull(classifier);

        var exception = Assert.Throws<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("{"u8, isFinalBlock: false, new JsonReaderState());
            reader.Read();
            classifier(ref reader);
        });

        Assert.Contains("Unexpected end", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_with_nested_union_case_refuses_undiscriminated_fallback()
    {
        // The nested union might accept the object itself, so the UndiscriminatedSourceType
        // case must not claim it.
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PointOrNestedUnion>("""{"text":"hello"}""", options));

        Assert.Contains(nameof(ShapeOrNote), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_routes_string_shaped_scalar_cases()
    {
        var options = CreateOptions();

        var date = JsonSerializer.Deserialize<ShapeOrScalars>("\"2026-01-02T03:04:05Z\"", options);
        var shape = JsonSerializer.Deserialize<ShapeOrScalars>("""{"$type":"circle-v1","r":1}""", options);
        var note = JsonSerializer.Deserialize<ShapeOrScalars>("""{"text":"hi"}""", options);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), date.Value);
        Assert.IsType<CircleV2>(shape.Value);
        Assert.IsType<Note>(note.Value);
    }

    [Fact]
    public void Union_case_with_converter_override_only_routes_through_discriminator()
    {
        var options = CreateOptions();

        var shape = JsonSerializer.Deserialize<ShapeOrStringEnum>("""{"$type":"circle-v1","r":1}""", options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrStringEnum>("\"Green\"", options));

        Assert.IsType<CircleV2>(shape.Value);
        Assert.Contains(nameof(Colour), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_case_with_options_level_converter_only_routes_through_discriminator()
    {
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrPlainEnum>("\"Green\"", options));

        Assert.Contains(nameof(PlainColour), exception.Message, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateOptions(Action<JsonMigrationBuilder>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(configure);
        return options;
    }

    // --- Test types ---

    public union Shape(CircleV2, RectangleV2);

    // The source generator requires a classifier type on unions whose cases share a JSON
    // value type, so the attribute route is what source-generated contexts use.
    [JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]
    public union AnnotatedShape(CircleV2, RectangleV2);

    public union ShapeOrScalar(CircleV2, string, int, bool);

    public union PointOrLabel(PointV2, string);

    public union ShapeOrNote(CircleV2, Note);

    public union ShapeOrTwoNotes(CircleV2, Note, OtherNote);

    public union ShapeOrCollections(CircleV2, List<int>, Dictionary<string, int>);

    public union ShapeOrStringOrChar(CircleV2, string, char);

    public union ShapeOrNestedUnion(CircleV2, ShapeOrNote);

    public union PointOrNestedUnion(PointV2, ShapeOrNote);

    public union ShapeOrScalars(CircleV2, DateTimeOffset, Note);

    [JsonConverter(typeof(JsonStringEnumConverter<Colour>))]
    public enum Colour
    {
        Red,
        Green,
    }

    public enum PlainColour
    {
        Red,
        Green,
    }

    public union ShapeOrStringEnum(CircleV2, Colour);

    public union ShapeOrPlainEnum(CircleV2, PlainColour);

    public union DuplicateDiscriminatorUnion(CircleV2, CircleV2Copy);

    public union TwoUndiscriminatedUnion(PointV2, PointV2Copy);

    public union PlainUnion(Note, string);

    [JsonMigratable(TypeDiscriminator = "circle-v2")]
    public record class CircleV2Copy(double Radius);

    public record class OtherLegacyPoint(int X, int Y);

    [JsonMigratable(TypeDiscriminator = "point-v2-copy", UndiscriminatedSourceType = typeof(OtherLegacyPoint))]
    public record class PointV2Copy(int X, int Y) : IMigrateFrom<OtherLegacyPoint, PointV2Copy>
    {
        public static bool TryMigrateFrom(OtherLegacyPoint source, out PointV2Copy result)
        {
            result = new PointV2Copy(source.X, source.Y);
            return true;
        }
    }

    [JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
    public union StructuralShapeOrNote(CircleV2, Note);

    [JsonMigratable(TypeDiscriminator = "circle-v1")]
    public record class CircleV1([property: JsonPropertyName("r")] double R);

    [JsonMigratable(TypeDiscriminator = "circle-v2")]
    public record class CircleV2(double Radius) : IJsonMigrationTracked, IMigrateFrom<CircleV1, CircleV2>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(CircleV1 source, out CircleV2 result)
        {
            result = new CircleV2(source.R);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "rect-v1")]
    public record class RectangleV1([property: JsonPropertyName("w")] int W, [property: JsonPropertyName("h")] int H);

    [JsonMigratable(TypeDiscriminator = "rect-v2")]
    public record class RectangleV2(int Width, int Height);

    public sealed class CircleMigrator : IMigrate<CircleV1, CircleV2>
    {
        public bool TryMigrateFrom(CircleV1 source, out CircleV2 result)
        {
            result = new CircleV2(source.R);
            return true;
        }
    }

    public sealed class RectangleMigrator : IMigrate<RectangleV1, RectangleV2>
    {
        public bool TryMigrateFrom(RectangleV1 source, out RectangleV2 result)
        {
            result = new RectangleV2(source.W, source.H);
            return true;
        }
    }

    public record class LegacyPoint(int X, int Y);

    [JsonMigratable(TypeDiscriminator = "point-v2", UndiscriminatedSourceType = typeof(LegacyPoint))]
    public record class PointV2(int X, int Y) : IJsonMigrationTracked, IMigrateFrom<LegacyPoint, PointV2>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(LegacyPoint source, out PointV2 result)
        {
            result = new PointV2(source.X, source.Y);
            return true;
        }
    }

    public record class Note(string Text);

    public record class OtherNote(string Text);

    [JsonMigratable(TypeDiscriminator = "drawing")]
    public record class Drawing(List<Shape> Shapes);

    [JsonPolymorphic]
    [JsonDerivedType(typeof(PolymorphicCircle), "poly-circle")]
    public abstract class PolymorphicShape;

    [JsonMigratable(TypeDiscriminator = "poly-circle")]
    public sealed class PolymorphicCircle : PolymorphicShape
    {
        public double Radius { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    // The injected discriminator is a string property, so a context that would otherwise
    // never see a string must register it explicitly.
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(AnnotatedShape))]
    [JsonSerializable(typeof(CircleV1))]
    [JsonSerializable(typeof(CircleV2))]
    [JsonSerializable(typeof(RectangleV2))]
    public partial class ShapeJsonContext : JsonSerializerContext;
}
#endif
