namespace Egil.SystemTextJson.Migration.Samples.FailureHandling;

[JsonMigratable(TypeDiscriminator = "data-v1")]
public record DataV1(string Value);

[JsonMigratable(TypeDiscriminator = "data-v2")]
public record DataV2(string Value, int Score)
    : IMigrateFrom<DataV1, DataV2>
{
    public static bool TryMigrateFrom(DataV1 source, out DataV2 result)
    {
        result = new DataV2(source.Value, 0);
        return true;
    }
}

#region failure_handling_return_null
// Per-type override:
[JsonMigratable(MigrationFailureHandling = JsonMigrationFailureHandling.ReturnNull)]
public record OptionalData(string Value);
#endregion

public class FailureHandlingTests
{
    [Fact]
    public void Default_failure_handling_throws()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"$type":"data-v1","value":"test"}""";
        var result = JsonSerializer.Deserialize<DataV2>(json, options)!;

        Assert.Equal("test", result.Value);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Fallback_to_target_type_via_builder()
    {
        #region failure_handling_builder
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(builder =>
        {
            builder.SetMigrationFailureHandling(
                JsonMigrationFailureHandling.FallBackToTargetType);
        });
        #endregion

        var json = """{"$type":"data-v2","value":"test","score":42}""";
        var result = JsonSerializer.Deserialize<DataV2>(json, options)!;

        Assert.Equal("test", result.Value);
        Assert.Equal(42, result.Score);
    }
}
