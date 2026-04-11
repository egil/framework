namespace Egil.SystemTextJson.Migration.Samples.ReadMigrateWriteBack;

[JsonMigratable(TypeDiscriminator = "document-v1")]
public record class DocumentV1(string Title, string Body);

#region read_migrate_write_back_types
[JsonMigratable(TypeDiscriminator = "document-v2")]
public record class DocumentV2 : IJsonMigrationTracked,
    IMigrateFrom<DocumentV1, DocumentV2>
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;

    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(DocumentV1 source, out DocumentV2 result)
    {
        result = new DocumentV2
        {
            Title = source.Title,
            Body = source.Body,
            Slug = source.Title.ToLowerInvariant().Replace(' ', '-'),
        };
        return true;
    }
}
#endregion

public sealed class ReadMigrateWriteBackSampleTests
{
    [Fact]
    public void Read_migrate_write_back_pattern()
    {
        #region read_migrate_write_back_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // Step 1: Read — old JSON from database/cache/file
        var storedJson = """{"$type":"document-v1","title":"Hello World","body":"Content here"}""";

        // Step 2: Deserialize — migration happens automatically
        var doc = JsonSerializer.Deserialize<DocumentV2>(storedJson, options);

        // Step 3: Check if migration occurred
        if (doc is { MigratedDuringDeserialization: true })
        {
            // Step 4: Write back — re-serialize in the current format
            var updatedJson = JsonSerializer.Serialize(doc, options);

            // Save updatedJson back to the database/cache/file.
            // Future reads won't need migration, improving performance.
            // updatedJson: {"$type":"document-v2","title":"Hello World",...}
        }
        #endregion

        Assert.NotNull(doc);
        Assert.True(doc.MigratedDuringDeserialization);
        Assert.Equal("hello-world", doc.Slug);

        var reserializedJson = JsonSerializer.Serialize(doc, options);
        Assert.Contains("\"$type\":\"document-v2\"", reserializedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_payload_does_not_trigger_write_back()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var currentJson = """{"$type":"document-v2","title":"Hello World","body":"Content","slug":"hello-world"}""";
        var doc = JsonSerializer.Deserialize<DocumentV2>(currentJson, options);

        Assert.NotNull(doc);
        Assert.False(doc.MigratedDuringDeserialization);
    }
}
