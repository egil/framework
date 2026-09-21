#if NET11_0_OR_GREATER
namespace Egil.SystemTextJson.Migration.Samples.UnionMigration;

#region union_migration_types
[JsonMigratable(TypeDiscriminator = "circle-v1")]
public record class CircleV1(double R);

[JsonMigratable(TypeDiscriminator = "circle-v2")]
public record class CircleV2(double Radius) : IMigrateFrom<CircleV1, CircleV2>
{
    public static bool TryMigrateFrom(CircleV1 source, out CircleV2 result)
    {
        result = new CircleV2(source.R);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "rectangle")]
public record class Rectangle(double Width, double Height);

// Each case carries its own [JsonMigratable] discriminator; the union itself needs no attribute
// when the options come from AddJsonMigrationSupport().
public union Shape(CircleV2, Rectangle);
#endregion

#region union_migration_source_gen_types
// Source-generated contexts must name the classifier on the union, because the generator
// rejects unions whose cases share a JSON value type unless a classifier is declared.
[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]
public union GeneratedShape(CircleV2, Rectangle);

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GeneratedShape))]
[JsonSerializable(typeof(CircleV1))]
public partial class ShapeJsonContext : JsonSerializerContext;
#endregion

public class UnionMigrationTests
{
    [Fact]
    public void Union_cases_are_selected_by_migration_discriminator()
    {
        #region union_migration_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Current payloads route to the case that owns the discriminator...
        var rectangle = JsonSerializer.Deserialize<Shape>("""{"$type":"rectangle","width":2,"height":3}""", options);

        // ...and old payloads route to the case that migrates them.
        var circle = JsonSerializer.Deserialize<Shape>("""{"$type":"circle-v1","r":1.5}""", options);

        // Writing a union writes the active case, including its discriminator.
        var json = JsonSerializer.Serialize(circle, options);
        // json: {"$type":"circle-v2","radius":1.5}
        #endregion

        Assert.IsType<Rectangle>(rectangle.Value);
        var migrated = Assert.IsType<CircleV2>(circle.Value);
        Assert.Equal(1.5, migrated.Radius);
        Assert.Equal("""{"$type":"circle-v2","radius":1.5}""", json);
    }

    [Fact]
    public void Union_with_source_generated_context()
    {
        #region union_migration_source_gen_usage
        var options = new JsonSerializerOptions(ShapeJsonContext.Default.Options);
        options.AddJsonMigrationSupport();

        var shape = JsonSerializer.Deserialize<GeneratedShape>("""{"$type":"circle-v1","R":1.5}""", options);
        #endregion

        var migrated = Assert.IsType<CircleV2>(shape.Value);
        Assert.Equal(1.5, migrated.Radius);
    }
}
#endif
