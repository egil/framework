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
    public void Nested_union_discriminators_route_to_the_nested_union_case()
    {
        // "rect-v2" belongs to a case of the inner union only; the outer union forwards it.
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<NoteOrShape>("""{"$type":"rect-v2","width":1,"height":2}""", options);
        var migrated = JsonSerializer.Deserialize<NoteOrShape>("""{"$type":"rect-v1","w":3,"h":4}""", CreateOptions(builder => builder.RegisterMigrator<RectangleV1, RectangleV2, RectangleMigrator>()));

        var inner = Assert.IsType<Shape>(value.Value);
        Assert.IsType<RectangleV2>(inner.Value);
        Assert.Equal(3, Assert.IsType<RectangleV2>(Assert.IsType<Shape>(migrated.Value).Value).Width);
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

    [Fact]
    public void Union_routes_non_object_migrator_sources_by_shape()
    {
        var options = CreateOptions();

        var number = JsonSerializer.Deserialize<ShapeOrCounter>("42", options);
        var list = JsonSerializer.Deserialize<ShapeOrCounter>("[1,2,3]", options);
        var dictionary = JsonSerializer.Deserialize<ShapeOrCounter>("""{"a":1,"b":2}""", options);
        var text = JsonSerializer.Deserialize<ShapeOrCounter>("\"seven\"", options);

        Assert.Equal(42, Assert.IsType<Counter>(number.Value).Value);
        Assert.Equal(3, Assert.IsType<Counter>(list.Value).Value);
        Assert.Equal(2, Assert.IsType<Counter>(dictionary.Value).Value);
        Assert.Equal("seven", text.Value);
    }

    [Fact]
    public void Union_migrator_source_shape_conflicting_with_plain_case_is_ambiguous()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CounterOrInt>("42", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Union_migrator_source_with_converter_override_only_routes_through_discriminator()
    {
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaintOrLabel>("\"Green\"", options));
        var paint = JsonSerializer.Deserialize<PaintOrLabel>("""{"$type":"paint","colour":"Red"}""", options);
        var synthetic = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaintOrLabel>($$"""{"$type":"{{typeof(PlainColour).FullName}}"}""", options));

        // A scalar source has no discriminator route: its type name is not a known discriminator.
        Assert.Contains(nameof(Paint), exception.Message, StringComparison.Ordinal);
        Assert.IsType<Paint>(paint.Value);
        Assert.Contains("No case", synthetic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_numeric_case_also_takes_strings_when_reading_numbers_from_strings_is_allowed()
    {
        var options = CreateOptions();
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        var quoted = JsonSerializer.Deserialize<CounterOrNote>("\"42\"", options);
        var plain = JsonSerializer.Deserialize<CounterOrNote>("42", options);

        Assert.Equal(42, Assert.IsType<Counter>(quoted.Value).Value);
        Assert.Equal(42, Assert.IsType<Counter>(plain.Value).Value);
    }

    [Fact]
    public void Union_string_case_wins_quoted_numbers_when_reading_numbers_from_strings_is_allowed()
    {
        var options = CreateOptions();
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        var value = JsonSerializer.Deserialize<ShapeOrCounter>("\"42\"", options);

        Assert.Equal("42", value.Value);
    }

    [Fact]
    public void Union_two_numeric_cases_are_ambiguous_for_quoted_numbers_when_reading_numbers_from_strings_is_allowed()
    {
        var options = CreateOptions();
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CounterOrInt>("\"42\"", options));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Union_object_source_with_converter_override_routes_by_discriminator()
    {
        var options = CreateOptions();

        // A non-migratable source is identified by its default discriminator, the type's full name.
        var value = JsonSerializer.Deserialize<BoxedOrLabel>($$"""{"$type":"{{typeof(LegacyBox).FullName}}","payload":"x"}""", options);
        var undiscriminated = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BoxedOrLabel>("""{"payload":"x"}""", options));
        var text = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BoxedOrLabel>("\"plain\"", options));

        // The override could read any token family, so nothing without a discriminator is routed.
        Assert.Equal("x", Assert.IsType<Boxed>(value.Value).Content);
        Assert.Contains(nameof(Boxed), undiscriminated.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Boxed), text.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_floating_point_case_takes_named_literals_when_allowed()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        options.AddJsonMigrationSupport();

        var value = JsonSerializer.Deserialize<ShapeOrDouble>("\"Infinity\"", options);
        var mixed = JsonSerializer.Deserialize<CounterOrDouble>("\"NaN\"", options);

        // Only the floating-point case takes named literals; the int-sourced case does not compete.
        Assert.Equal(double.PositiveInfinity, value.Value);
        Assert.True(double.IsNaN(Assert.IsType<double>(mixed.Value)));
    }

    [Fact]
    public void Union_rejects_migratable_case_served_by_an_earlier_converter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new BypassingCircleConverter());
        options.AddJsonMigrationSupport();

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v2","radius":1}""", options));

        Assert.Contains(nameof(BypassingCircleConverter), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_overridden_dictionary_source_keeps_its_discriminator_route()
    {
        var options = CreateOptions();
        options.Converters.Add(new TaggedDictionaryConverter());

        var value = JsonSerializer.Deserialize<TalliedOrLabel>($$"""{"$type":"{{typeof(Dictionary<string, int>).FullName}}","a":1,"b":2}""", options);

        Assert.Equal(2, Assert.IsType<Tallied>(value.Value).Count);
    }

    [Fact]
    public void Union_named_literal_skips_integer_sourced_case_when_reading_numbers_from_strings()
    {
        // Web defaults enable AllowReadingFromString for both cases, but only the double case
        // can read "NaN"; the int-sourced case must not make it ambiguous.
        var options = CreateOptions();

        var named = JsonSerializer.Deserialize<CounterOrDouble>("\"NaN\"", options);
        var quoted = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CounterOrDouble>("\"42\"", options));

        Assert.True(double.IsNaN(Assert.IsType<double>(named.Value)));
        Assert.Contains("ambiguous", quoted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Collection_of_unions_competes_with_other_collection_sources_for_every_element_shape()
    {
        // A union element may accept the primitive too, so the int list cannot be chosen by shape.
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UnionListOrIntListState>("[1]", options));
        var alone = JsonSerializer.Deserialize<UnionListState>("[1]", options);

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, alone!.Count);
    }

    [Fact]
    public void Union_overridden_dictionary_interface_source_keeps_its_discriminator_route()
    {
        var options = CreateOptions();
        options.Converters.Add(new TaggedReadOnlyDictionaryConverter());

        var value = JsonSerializer.Deserialize<ReadOnlyTalliedOrLabel>($$"""{"$type":"{{typeof(IReadOnlyDictionary<string, int>).FullName}}","a":1}""", options);

        Assert.Equal(1, Assert.IsType<ReadOnlyTallied>(value.Value).Count);
    }

    [Fact]
    public void Union_overridden_collection_source_keeps_its_discriminator_route()
    {
        var options = CreateOptions();
        options.Converters.Add(new TaggedIntListConverter());

        var value = JsonSerializer.Deserialize<SummedOrLabel>($$"""{"$type":"{{typeof(IntList).FullName}}","values":[1,2,3]}""", options);

        Assert.Equal(6, Assert.IsType<Summed>(value.Value).Total);
    }

    [Fact]
    public void Union_overridden_object_case_is_reachable_through_its_discriminator()
    {
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<ShapeOrLegacyBox>($$"""{"$type":"{{typeof(LegacyBox).FullName}}","payload":"x"}""", options);
        var undiscriminated = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShapeOrLegacyBox>("""{"payload":"x"}""", options));

        Assert.Equal("x", Assert.IsType<LegacyBox>(value.Value).Payload);
        Assert.Contains(nameof(LegacyBox), undiscriminated.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sibling_nested_unions_claiming_the_same_discriminator_fail_at_configuration()
    {
        var options = CreateOptions();

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<TwoNestedShapes>("""{"$type":"circle-v2","radius":1}""", options));

        Assert.Contains("circle-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_union_scalar_source_with_converter_override_is_not_forwarded()
    {
        var options = CreateOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NoteOrPaintUnion>($$"""{"$type":"{{typeof(PlainColour).FullName}}"}""", options));
        var paint = JsonSerializer.Deserialize<NoteOrPaintUnion>("""{"$type":"paint","colour":"Red"}""", options);

        Assert.Contains("No case", exception.Message, StringComparison.Ordinal);
        Assert.IsType<Paint>(Assert.IsType<PaintOrLabel>(paint.Value).Value);
    }

    [Fact]
    public void Union_typed_migrator_source_is_not_advertised_as_a_route()
    {
        // WrappedShape migrates from the Shape union; the migration converter cannot identify a
        // union payload by discriminator, so the outer union must not forward Shape's cases.
        var options = CreateOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WrappedShapeOrLabel>("""{"$type":"circle-v2","radius":1}""", options));

        Assert.Contains("No case", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_union_round_trips_and_migrates_nested_old_payloads()
    {
        // Branch holds Node children, so configuring Node's classifier happens again inside the
        // options the migration factory clones while it builds Branch's own converter.
        var options = CreateOptions();
        Node tree = new Branch([new Leaf("a"), new Branch([])]);

        var json = JsonSerializer.Serialize(tree, options);
        var migrated = JsonSerializer.Deserialize<Node>("""{"$type":"branch","children":[{"$type":"leaf-v1","txt":"old"}]}""", options);

        Assert.Equal("""{"$type":"branch","children":[{"$type":"leaf","text":"a"},{"$type":"branch","children":[]}]}""", json);
        Assert.IsType<Branch>(JsonSerializer.Deserialize<Node>(json, options).Value);
        var leaf = Assert.IsType<Leaf>(Assert.Single(Assert.IsType<Branch>(migrated.Value).Children).Value);
        Assert.Equal("old", leaf.Text);
    }

    [Fact]
    public void Nullable_migratable_case_round_trips_through_its_discriminator()
    {
        var options = CreateOptions();
        NullableValueLeafOrLeaf value = new ValueLeaf(42);

        var json = JsonSerializer.Serialize(value, options);
        var roundTripped = JsonSerializer.Deserialize<NullableValueLeafOrLeaf>(json, options);
        var migrated = JsonSerializer.Deserialize<NullableValueLeafOrLeaf>("""{"$type":"value-leaf-v1","n":7}""", options);

        Assert.Equal("""{"$type":"value-leaf","number":42}""", json);
        Assert.Equal(42, Assert.IsType<ValueLeaf>(roundTripped.Value).Number);
        Assert.Equal(7, Assert.IsType<ValueLeaf>(migrated.Value).Number);
    }

    [Fact]
    public void Nested_union_forwards_overridden_object_case_discriminators()
    {
        // LegacyBox is reachable through its discriminator as a direct case of LeafOrLegacyBox,
        // so an outer union must forward that route as well.
        var options = CreateOptions();
        var json = $$"""{"$type":"{{typeof(LegacyBox).FullName}}","payload":"kept"}""";

        var value = JsonSerializer.Deserialize<LeafOrLegacyBoxOrLabel>(json, options);

        var inner = Assert.IsType<LeafOrLegacyBox>(value.Value);
        Assert.Equal("kept", Assert.IsType<LegacyBox>(inner.Value).Payload);
    }

    [Fact]
    public void Union_nullable_migratable_source_routes_by_the_underlying_discriminator()
    {
        var options = CreateOptions();

        var value = JsonSerializer.Deserialize<WrappedLeafOrLabel>("""{"$type":"value-leaf","number":5}""", options);

        Assert.Equal(5, Assert.IsType<WrappedLeaf>(value.Value).Number);
    }

    [Fact]
    public void Union_overridden_numeric_struct_source_keeps_its_discriminator_route()
    {
        // A custom converter decides the JSON shape of a numeric struct; this one writes a
        // discriminated object, so the route must stay available.
        var options = CreateOptions();
        options.Converters.Add(new ObjectComplexConverter());

        var value = JsonSerializer.Deserialize<ComplexTargetOrLabel>($$"""{"$type":"{{typeof(System.Numerics.Complex).FullName}}","real":3,"imaginary":4}""", options);

        Assert.Equal(5, Assert.IsType<ComplexTarget>(value.Value).Magnitude);
    }

    [Fact]
    public void Union_rejects_nullable_migratable_case_served_by_a_nullable_converter()
    {
        var options = CreateOptions();
        options.Converters.Add(new SentinelNullableValueLeafConverter());

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<NullableValueLeafOrLeaf>("""{"$type":"value-leaf-v1","n":7}""", options));

        Assert.Contains(nameof(SentinelNullableValueLeafConverter), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_model_migrates_every_level_when_the_old_model_uses_old_types()
    {
        var options = CreateOptions();
        OldTreeNode oldTree = new OldTreeBranch("root", [new OldTreeBranch("child", [new Leaf("x")])]);

        var json = JsonSerializer.Serialize(oldTree, options);
        var tree = JsonSerializer.Deserialize<TreeNode>(json, options);

        var root = Assert.IsType<TreeBranch>(tree.Value);
        var child = Assert.IsType<TreeBranch>(Assert.Single(root.Children).Value);
        Assert.Equal("root", root.Label);
        Assert.Equal("child", child.Label);
        Assert.Equal("x", Assert.IsType<Leaf>(Assert.Single(child.Children).Value).Text);
    }

    [Fact]
    public void Recursive_source_referencing_the_current_union_refuses_nested_old_payloads()
    {
        // BranchWire declares its children as the current TreeNode union. Inside TreeBranch's own
        // migration TreeBranch resolves to its plain contract, so a nested "tree-v1" cannot be
        // migrated; it must be refused rather than read as a current TreeBranch.
        var options = CreateOptions();
        var json = """{"$type":"tree-v1-wire","oldName":"root","children":[{"$type":"tree-v1-wire","oldName":"child","children":[]}]}""";

        var flat = JsonSerializer.Deserialize<WireTreeNode>("""{"$type":"tree-v1-wire","oldName":"root","children":[{"$type":"leaf","text":"x"}]}""", options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WireTreeNode>(json, options));

        Assert.Equal("root", Assert.IsType<WireTreeBranch>(flat.Value).Label);
        Assert.Contains("'tree-v1-wire'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(WireTreeBranch), exception.Message, StringComparison.Ordinal);
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

    public union NoteOrShape(Note, Shape);

    public union ShapeAgain(CircleV2, Note);

    public union TwoNestedShapes(Shape, ShapeAgain);

    public union NoteOrPaintUnion(Note, PaintOrLabel);

    [JsonMigratable(TypeDiscriminator = "wrapped-shape")]
    public record class WrappedShape(string Kind) : IMigrateFrom<Shape, WrappedShape>
    {
        public static bool TryMigrateFrom(Shape source, out WrappedShape result)
        {
            result = new WrappedShape(source.Value?.GetType().Name ?? "none");
            return true;
        }
    }

    public union WrappedShapeOrLabel(WrappedShape, string);

    public union PointOrNestedUnion(PointV2, ShapeOrNote);

    public union ShapeOrScalars(CircleV2, DateTimeOffset, Note);

    [JsonMigratable(TypeDiscriminator = "counter")]
    public record class Counter(int Value)
        : IMigrateFrom<int, Counter>,
          IMigrateFrom<List<int>, Counter>,
          IMigrateFrom<Dictionary<string, int>, Counter>
    {
        public static bool TryMigrateFrom(int source, out Counter result)
        {
            result = new Counter(source);
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out Counter result)
        {
            result = new Counter(source.Count);
            return true;
        }

        public static bool TryMigrateFrom(Dictionary<string, int> source, out Counter result)
        {
            result = new Counter(source.Count);
            return true;
        }
    }

    public union ShapeOrCounter(Counter, string);

    [JsonMigratable(TypeDiscriminator = "paint")]
    public record class Paint(PlainColour Colour) : IMigrateFrom<PlainColour, Paint>
    {
        public static bool TryMigrateFrom(PlainColour source, out Paint result)
        {
            result = new Paint(source);
            return true;
        }
    }

    public union PaintOrLabel(Paint, string);

    [JsonConverter(typeof(LegacyBoxConverter))]
    public sealed class LegacyBox
    {
        public string Payload { get; set; } = string.Empty;
    }

    // Reads {"$type":"<full name>","payload":"..."} by hand so the source has a converter override.
    public sealed class LegacyBoxConverter : JsonConverter<LegacyBox>
    {
        public override LegacyBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var box = new LegacyBox();
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals("payload"u8))
                {
                    reader.Read();
                    box.Payload = reader.GetString()!;
                }
                else
                {
                    reader.Skip();
                }
            }

            return box;
        }

        public override void Write(Utf8JsonWriter writer, LegacyBox value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("$type", typeof(LegacyBox).FullName);
            writer.WriteString("payload", value.Payload);
            writer.WriteEndObject();
        }
    }

    [JsonMigratable(TypeDiscriminator = "boxed")]
    public record class Boxed(string Content) : IMigrateFrom<LegacyBox, Boxed>
    {
        public static bool TryMigrateFrom(LegacyBox source, out Boxed result)
        {
            result = new Boxed(source.Payload);
            return true;
        }
    }

    public union BoxedOrLabel(Boxed, string);

    public union ShapeOrLegacyBox(CircleV2, LegacyBox);

    public sealed class BypassingCircleConverter : JsonConverter<CircleV2>
    {
        public override CircleV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return new CircleV2(0);
        }

        public override void Write(Utf8JsonWriter writer, CircleV2 value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    // Reads {"$type":"...","a":1,...} into a dictionary, skipping the discriminator.
    public sealed class TaggedDictionaryConverter : JsonConverter<Dictionary<string, int>>
    {
        public override Dictionary<string, int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                if (reader.TokenType is JsonTokenType.Number)
                {
                    result[key] = reader.GetInt32();
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, int> value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    [JsonMigratable(TypeDiscriminator = "tallied")]
    public record class Tallied(int Count) : IMigrateFrom<Dictionary<string, int>, Tallied>
    {
        public static bool TryMigrateFrom(Dictionary<string, int> source, out Tallied result)
        {
            result = new Tallied(source.Count);
            return true;
        }
    }

    public union TalliedOrLabel(Tallied, string);

    public sealed class TaggedReadOnlyDictionaryConverter : JsonConverter<IReadOnlyDictionary<string, int>>
    {
        public override IReadOnlyDictionary<string, int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                if (reader.TokenType is JsonTokenType.Number)
                {
                    result[key] = reader.GetInt32();
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, int> value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    [JsonMigratable(TypeDiscriminator = "readonly-tallied")]
    public record class ReadOnlyTallied(int Count) : IMigrateFrom<IReadOnlyDictionary<string, int>, ReadOnlyTallied>
    {
        public static bool TryMigrateFrom(IReadOnlyDictionary<string, int> source, out ReadOnlyTallied result)
        {
            result = new ReadOnlyTallied(source.Count);
            return true;
        }
    }

    public union ReadOnlyTalliedOrLabel(ReadOnlyTallied, string);

    public sealed class IntList : List<int>;

    // Reads {"$type":"...","values":[...]} into a derived list, so an enumerable source is read
    // from a discriminated object through a converter override.
    public sealed class TaggedIntListConverter : JsonConverter<IntList>
    {
        public override IntList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new IntList();
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals("values"u8))
                {
                    reader.Read();
                    result.AddRange(JsonSerializer.Deserialize<List<int>>(ref reader, options)!);
                }
                else
                {
                    reader.Skip();
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, IntList value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    [JsonMigratable(TypeDiscriminator = "summed")]
    public record class Summed(int Total) : IMigrateFrom<IntList, Summed>
    {
        public static bool TryMigrateFrom(IntList source, out Summed result)
        {
            result = new Summed(source.Sum());
            return true;
        }
    }

    public union SummedOrLabel(Summed, string);

    public union CounterOrInt(Counter, int);

    public union CounterOrNote(Counter, Note);

    public union ShapeOrDouble(CircleV2, double);

    public union CounterOrDouble(Counter, double);

    [JsonMigratable]
    public record class UnionListOrIntListState(string Source)
        : IMigrateFrom<List<ShapeOrScalar>, UnionListOrIntListState>,
          IMigrateFrom<List<int>, UnionListOrIntListState>
    {
        public static bool TryMigrateFrom(List<ShapeOrScalar> source, out UnionListOrIntListState result)
        {
            result = new UnionListOrIntListState("from-union-list");
            return true;
        }

        public static bool TryMigrateFrom(List<int> source, out UnionListOrIntListState result)
        {
            result = new UnionListOrIntListState("from-int-list");
            return true;
        }
    }

    [JsonMigratable]
    public record class UnionListState(int Count) : IMigrateFrom<List<ShapeOrScalar>, UnionListState>
    {
        public static bool TryMigrateFrom(List<ShapeOrScalar> source, out UnionListState result)
        {
            result = new UnionListState(source.Count);
            return true;
        }
    }

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

    [JsonMigratable(TypeDiscriminator = "leaf-v1")]
    public record class LeafV1([property: JsonPropertyName("txt")] string Txt);

    [JsonMigratable(TypeDiscriminator = "leaf")]
    public record class Leaf(string Text) : IMigrateFrom<LeafV1, Leaf>
    {
        public static bool TryMigrateFrom(LeafV1 source, out Leaf result)
        {
            result = new Leaf(source.Txt);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "branch")]
    public record class Branch(Node[] Children);

    public union Node(Branch, Leaf);

    [JsonMigratable(TypeDiscriminator = "value-leaf-v1")]
    public record struct ValueLeafV1([property: JsonPropertyName("n")] int N);

    [JsonMigratable(TypeDiscriminator = "value-leaf")]
    public record struct ValueLeaf(int Number) : IMigrateFrom<ValueLeafV1, ValueLeaf>
    {
        public static bool TryMigrateFrom(ValueLeafV1 source, out ValueLeaf result)
        {
            result = new ValueLeaf(source.N);
            return true;
        }
    }

    public union NullableValueLeafOrLeaf(ValueLeaf?, Leaf);

    public union LeafOrLegacyBox(Leaf, LegacyBox);

    public union LeafOrLegacyBoxOrLabel(LeafOrLegacyBox, string);

    [JsonMigratable(TypeDiscriminator = "wrapped-leaf")]
    public record class WrappedLeaf(int Number) : IMigrateFrom<ValueLeaf?, WrappedLeaf>
    {
        public static bool TryMigrateFrom(ValueLeaf? source, out WrappedLeaf result)
        {
            result = new WrappedLeaf(source?.Number ?? -1);
            return true;
        }
    }

    public union WrappedLeafOrLabel(WrappedLeaf, string);

    [JsonMigratable(TypeDiscriminator = "complex-target")]
    public record class ComplexTarget(double Magnitude) : IMigrateFrom<System.Numerics.Complex, ComplexTarget>
    {
        public static bool TryMigrateFrom(System.Numerics.Complex source, out ComplexTarget result)
        {
            result = new ComplexTarget(source.Magnitude);
            return true;
        }
    }

    public union ComplexTargetOrLabel(ComplexTarget, string);

    // Writes {"$type":"<full name>","real":..,"imaginary":..} so the numeric struct is an object on the wire.
    public sealed class ObjectComplexConverter : JsonConverter<System.Numerics.Complex>
    {
        public override System.Numerics.Complex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            double real = 0, imaginary = 0;
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals("real"u8))
                {
                    reader.Read();
                    real = reader.GetDouble();
                }
                else if (reader.ValueTextEquals("imaginary"u8))
                {
                    reader.Read();
                    imaginary = reader.GetDouble();
                }
                else
                {
                    reader.Skip();
                }
            }

            return new System.Numerics.Complex(real, imaginary);
        }

        public override void Write(Utf8JsonWriter writer, System.Numerics.Complex value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("$type", typeof(System.Numerics.Complex).FullName);
            writer.WriteNumber("real", value.Real);
            writer.WriteNumber("imaginary", value.Imaginary);
            writer.WriteEndObject();
        }
    }

    public sealed class SentinelNullableValueLeafConverter : JsonConverter<ValueLeaf?>
    {
        public override ValueLeaf? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return new ValueLeaf(-999);
        }

        public override void Write(Utf8JsonWriter writer, ValueLeaf? value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    // Old model authored with old types all the way down: nested old nodes are read as the old
    // type and the migrator walks the children.
    [JsonMigratable(TypeDiscriminator = "tree-v1")]
    public record class OldTreeBranch(string OldName, OldTreeNode[] Children);

    public union OldTreeNode(OldTreeBranch, Leaf);

    [JsonMigratable(TypeDiscriminator = "tree-v2")]
    public record class TreeBranch(string Label, TreeNode[] Children) : IMigrateFrom<OldTreeBranch, TreeBranch>
    {
        public static bool TryMigrateFrom(OldTreeBranch source, out TreeBranch result)
        {
            result = new TreeBranch(source.OldName, [.. source.Children.Select(static child => child.Value switch
            {
                OldTreeBranch branch => TryMigrateFrom(branch, out TreeBranch migrated) ? (TreeNode)migrated : throw new InvalidOperationException(),
                Leaf leaf => leaf,
                _ => throw new InvalidOperationException(),
            })]);
            return true;
        }
    }

    public union TreeNode(TreeBranch, Leaf);

    // Old wire type that reuses the current union for its children.
    [JsonMigratable(TypeDiscriminator = "tree-v1-wire")]
    public record class WireTreeBranchV1(string OldName, WireTreeNode[] Children);

    [JsonMigratable(TypeDiscriminator = "tree-v2-wire")]
    public record class WireTreeBranch(string Label, WireTreeNode[] Children) : IMigrateFrom<WireTreeBranchV1, WireTreeBranch>
    {
        public static bool TryMigrateFrom(WireTreeBranchV1 source, out WireTreeBranch result)
        {
            result = new WireTreeBranch(source.OldName, source.Children);
            return true;
        }
    }

    public union WireTreeNode(WireTreeBranch, Leaf);

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
