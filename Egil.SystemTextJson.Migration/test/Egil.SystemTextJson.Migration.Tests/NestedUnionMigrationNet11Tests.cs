#if NET11_0_OR_GREATER
using System.Text.Json;
using static Egil.SystemTextJson.Migration.Tests.UnionMigrationTests;

namespace Egil.SystemTextJson.Migration.Tests;

public class NestedUnionMigrationTests
{
    [Fact]
    public void Migration_discriminators_are_forwarded_through_multiple_nested_unions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();

        var value = JsonSerializer.Deserialize<DeepShape>("""{"$type":"circle-v1","r":3}""", options);

        var middle = Assert.IsType<NoteOrShape>(value.Value);
        var shape = Assert.IsType<Shape>(middle.Value);
        var circle = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(3, circle.Radius);
        Assert.True(circle.MigratedDuringDeserialization);
    }

    [Fact]
    public void Nested_union_forwards_an_object_source_read_by_a_custom_converter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();
        var json = $$"""{"$type":"{{typeof(LegacyBox).FullName}}","payload":"preserved"}""";

        var value = JsonSerializer.Deserialize<NestedBoxed>(json, options);

        var nested = Assert.IsType<BoxedOrLabel>(value.Value);
        Assert.Equal("preserved", Assert.IsType<Boxed>(nested.Value).Content);
    }

    [Fact]
    public void Nested_union_forwards_a_plain_object_source_discriminator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();
        var json = $$"""{"$type":"{{typeof(LegacyPoint).FullName}}","x":4,"y":5}""";

        var value = JsonSerializer.Deserialize<NestedPoint>(json, options);

        var nested = Assert.IsType<PointOrLabel>(value.Value);
        var point = Assert.IsType<PointV2>(nested.Value);
        Assert.Equal((4, 5), (point.X, point.Y));
        Assert.True(point.MigratedDuringDeserialization);
    }

    [Fact]
    public void Nested_union_does_not_advertise_scalar_sources_as_object_discriminators()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();

        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<NestedCounter>("""{"$type":"System.Int32","value":42}""", options));

        Assert.Contains("No case", exception.Message, StringComparison.Ordinal);
        Assert.Contains("matches discriminator 'System.Int32'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_classifier_leaves_nested_unions_without_migratable_cases_alone()
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();
        options.MakeReadOnly(populateMissingResolver: true);

        var contract = options.GetTypeInfo(typeof(NestedPlain));

        Assert.Null(contract.TypeClassifier);
    }

    public union DeepShape(NoteOrShape, int);

    public union NestedBoxed(BoxedOrLabel, bool);

    public union NestedPoint(PointOrLabel, bool);

    public union NestedCounter(CounterOrNote, bool);

    public union NestedPlain(PlainUnion, bool);
}
#endif
