using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// <c>[JsonMigratable]</c> injects a discriminator property, which only JSON-object contracts
/// can carry. Other contract kinds must fail with a diagnostic that says so instead of the
/// generic STJ "invalid JsonTypeInfo operation" error.
/// </summary>
public partial class UnsupportedTargetKindTests
{
    [Fact]
    public void Migratable_collection_target_throws_not_supported_with_guidance()
    {
        var options = CreateOptions();

        var exception = Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Deserialize<IntListTarget>("[1,2]", options));

        Assert.Contains(typeof(IntListTarget).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Enumerable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("JSON object", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migratable_collection_target_throws_not_supported_on_serialize()
    {
        var options = CreateOptions();

        var exception = Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Serialize(new IntListTarget { 1 }, options));

        Assert.Contains("JSON object", exception.Message, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    [JsonMigratable]
    public class IntListTarget : List<int>;
}
