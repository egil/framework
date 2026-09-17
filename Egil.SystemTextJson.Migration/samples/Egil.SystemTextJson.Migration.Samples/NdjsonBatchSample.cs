#if NET11_0_OR_GREATER
using System.Text;

namespace Egil.SystemTextJson.Migration.Samples.NdjsonBatch;

#region ndjson_batch_types
[JsonMigratable(TypeDiscriminator = "event-v1")]
public record class EventV1(string Name, string When);

[JsonMigratable(TypeDiscriminator = "event-v2")]
public record class EventV2(string Name, DateTimeOffset OccurredAt) : IJsonMigrationTracked,
    IMigrateFrom<EventV1, EventV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(EventV1 source, out EventV2 result)
    {
        result = new EventV2(source.Name, DateTimeOffset.Parse(source.When, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}
#endregion

public class NdjsonBatchTests
{
    [Fact]
    public async Task Migrate_ndjson_stream_and_write_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ndjson = """
            {"$type":"event-v1","name":"signup","when":"2026-01-01T10:00:00Z"}
            {"$type":"event-v2","name":"login","occurredAt":"2026-01-02T10:00:00+00:00"}

            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));
        using var output = new MemoryStream();

        #region ndjson_batch_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        // topLevelValues: true reads newline-delimited JSON one record at a time. Each record is
        // migrated as it is read, so the write-back stream only ever contains the current format.
        var migratedCount = 0;
        var records = JsonSerializer.DeserializeAsyncEnumerable<EventV2>(input, topLevelValues: true, options, cancellationToken);

        await JsonSerializer.SerializeAsyncEnumerable(output, TrackMigrations(records), topLevelValues: true, options, cancellationToken);

        async IAsyncEnumerable<EventV2> TrackMigrations(IAsyncEnumerable<EventV2?> source)
        {
            await foreach (var record in source)
            {
                if (record is null)
                {
                    continue;
                }

                if (record.MigratedDuringDeserialization)
                {
                    migratedCount++;
                }

                yield return record;
            }
        }
        #endregion

        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal(1, migratedCount);
        Assert.Equal(2, written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("event-v1", written, StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"event-v2\",\"name\":\"signup\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_record_version_fails_the_batch_at_that_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        var ndjson = """
            {"$type":"event-v2","name":"login","occurredAt":"2026-01-02T10:00:00+00:00"}
            {"$type":"event-v0","name":"legacy"}

            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));

        // The reader parses a buffer at a time, so the failure can surface before earlier
        // records in the same buffer are yielded; only the exception is a reliable signal.
        var exception = await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in JsonSerializer.DeserializeAsyncEnumerable<EventV2>(input, topLevelValues: true, options, cancellationToken))
            {
            }
        });

        Assert.Contains("event-v0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_stops_the_batch()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("""{"$type":"event-v2","name":"login","occurredAt":"2026-01-02T10:00:00+00:00"}"""));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in JsonSerializer.DeserializeAsyncEnumerable<EventV2>(input, topLevelValues: true, options, cancellation.Token))
            {
            }
        });
    }
}
#endif
