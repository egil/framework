namespace Egil.SystemTextJson.Migration.Samples.Telemetry;

using System.Diagnostics.Metrics;

[JsonMigratable(TypeDiscriminator = "item-v1")]
public record ItemV1(string Name);

[JsonMigratable(TypeDiscriminator = "item-v2")]
public record ItemV2(string Name, string Category)
    : IMigrateFrom<ItemV1, ItemV2>
{
    public static bool TryMigrateFrom(ItemV1 source, out ItemV2 result)
    {
        result = new ItemV2(source.Name, "default");
        return true;
    }
}

public class TelemetryTests
{
    [Fact]
    public void Migration_counter_fires_on_migration()
    {
        #region otel_meter_listener
        // Subscribe to the migration meter using MeterListener:
        using var meterListener = new MeterListener();
        var migrationCount = 0L;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == JsonMigrationTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                if (instrument.Name == JsonMigrationTelemetry.MigrationCounterName)
                {
                    Interlocked.Add(ref migrationCount, measurement);
                }
            });
        meterListener.Start();
        #endregion

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"$type":"item-v1","name":"Widget"}""";
        var item = JsonSerializer.Deserialize<ItemV2>(json, options)!;

        Assert.Equal("Widget", item.Name);
        Assert.Equal("default", item.Category);
        Assert.True(migrationCount > 0, "Migration counter should have fired");
    }
}
