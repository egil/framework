using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public class TelemetryTests : IDisposable
{
    private readonly MeterListener listener;
    private readonly ConcurrentQueue<MeasurementRecord> measurements = new();

    public TelemetryTests()
    {
        listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JsonMigrationTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagDict = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                tagDict[tag.Key] = tag.Value;
            }

            measurements.Enqueue(new MeasurementRecord(instrument.Name, measurement, tagDict));
        });
        listener.Start();
    }

    public void Dispose()
    {
        listener.Dispose();
        GC.SuppressFinalize(this);
    }

    private List<MeasurementRecord> GetMeasurementsForTarget(string targetTypeName)
        => measurements
            .Where(m => m.Tags.TryGetValue("stjm.target_type", out var t) && (string?)t == targetTypeName)
            .ToList();

    [Fact]
    public void Successful_migration_increments_counter_with_correct_tags()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new TelV1("Egil Hansen", 42), options);
        JsonSerializer.Deserialize<TelV2>(json, options);

        var relevant = GetMeasurementsForTarget(typeof(TelV2).FullName!);
        var migration = Assert.Single(relevant);
        Assert.Equal(JsonMigrationTelemetry.MigrationCounterName, migration.InstrumentName);
        Assert.Equal(1, migration.Value);
        Assert.Equal(typeof(TelV1).FullName, migration.Tags["stjm.source_type"]);
        Assert.Equal(typeof(TelV2).FullName, migration.Tags["stjm.target_type"]);
        Assert.Equal("success", migration.Tags["stjm.migration.status"]);
    }

    [Fact]
    public void Failed_migration_with_fallback_increments_counter_with_failure_status()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder
                .SetMigrationFailureHandling(JsonMigrationFailureHandling.FallBackToTargetType)
                .RegisterMigrator<TelFailFallbackMigrator>());

        var json = JsonSerializer.Serialize(new TelFailFallbackV1("data"), options);
        JsonSerializer.Deserialize<TelFailFallbackV2>(json, options);

        var relevant = GetMeasurementsForTarget(typeof(TelFailFallbackV2).FullName!);
        var migration = Assert.Single(relevant);
        Assert.Equal(JsonMigrationTelemetry.MigrationCounterName, migration.InstrumentName);
        Assert.Equal(1, migration.Value);
        Assert.Equal(typeof(TelFailFallbackV1).FullName, migration.Tags["stjm.source_type"]);
        Assert.Equal(typeof(TelFailFallbackV2).FullName, migration.Tags["stjm.target_type"]);
        Assert.Equal("failure", migration.Tags["stjm.migration.status"]);
    }

    [Fact]
    public void Failed_migration_with_return_null_increments_counter_with_failure_status()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
            builder
                .SetMigrationFailureHandling(JsonMigrationFailureHandling.ReturnNull)
                .RegisterMigrator<TelFailReturnNullMigrator>());

        var json = JsonSerializer.Serialize(new TelFailReturnNullV1("data"), options);
        JsonSerializer.Deserialize<TelFailReturnNullV2>(json, options);

        var relevant = GetMeasurementsForTarget(typeof(TelFailReturnNullV2).FullName!);
        var migration = Assert.Single(relevant);
        Assert.Equal("failure", migration.Tags["stjm.migration.status"]);
    }

    [Fact]
    public void Deserialize_target_type_does_not_increment_counter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new TelNoMigrateV1("Egil", 42), options);
        JsonSerializer.Deserialize<TelNoMigrateV1>(json, options);

        var relevant = GetMeasurementsForTarget(typeof(TelNoMigrateV1).FullName!);
        Assert.Empty(relevant);
    }

    [Fact]
    public void Deserialize_legacy_payload_does_not_increment_counter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json = """{"name":"Egil","age":42}""";
        JsonSerializer.Deserialize<TelNoMigrateV1>(json, options);

        var relevant = GetMeasurementsForTarget(typeof(TelNoMigrateV1).FullName!);
        Assert.Empty(relevant);
    }

    [Fact]
    public void Multiple_migrations_increments_counter_for_each()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();

        var json1 = JsonSerializer.Serialize(new TelMultiV1("Egil Hansen", 42), options);
        var json2 = JsonSerializer.Serialize(new TelMultiV1("Jane Doe", 30), options);

        JsonSerializer.Deserialize<TelMultiV2>(json1, options);
        JsonSerializer.Deserialize<TelMultiV2>(json2, options);

        var relevant = GetMeasurementsForTarget(typeof(TelMultiV2).FullName!);
        Assert.Equal(2, relevant.Count);
        Assert.All(relevant, m =>
        {
            Assert.Equal(JsonMigrationTelemetry.MigrationCounterName, m.InstrumentName);
            Assert.Equal(1, m.Value);
            Assert.Equal("success", m.Tags["stjm.migration.status"]);
        });
    }

    private sealed record MeasurementRecord(
        string InstrumentName,
        long Value,
        Dictionary<string, object?> Tags);

    [JsonMigratable]
    public record class TelV1(string Name, int Age);

    [JsonMigratable]
    public record class TelV2(string FirstName, string LastName, int Age) :
        IMigrateFrom<TelV1, TelV2>
    {
        public static bool TryMigrateFrom(TelV1 source, out TelV2 result)
        {
            var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = new TelV2(
                names.Length > 0 ? names[0] : string.Empty,
                names.Length > 1 ? names[1] : string.Empty,
                source.Age);
            return true;
        }
    }

    [JsonMigratable]
    public record class TelMultiV1(string Name, int Age);

    [JsonMigratable]
    public record class TelMultiV2(string FirstName, string LastName, int Age) :
        IMigrateFrom<TelMultiV1, TelMultiV2>
    {
        public static bool TryMigrateFrom(TelMultiV1 source, out TelMultiV2 result)
        {
            var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = new TelMultiV2(
                names.Length > 0 ? names[0] : string.Empty,
                names.Length > 1 ? names[1] : string.Empty,
                source.Age);
            return true;
        }
    }

    [JsonMigratable]
    public record class TelFailFallbackV1(string Data);

    [JsonMigratable]
    public record class TelFailFallbackV2(string Data);

    public sealed class TelFailFallbackMigrator : IMigrate<TelFailFallbackV1, TelFailFallbackV2>
    {
        public bool TryMigrateFrom(TelFailFallbackV1 source, out TelFailFallbackV2 result)
        {
            result = default!;
            return false;
        }
    }

    [JsonMigratable]
    public record class TelFailReturnNullV1(string Data);

    [JsonMigratable]
    public record class TelFailReturnNullV2(string Data);

    public sealed class TelFailReturnNullMigrator : IMigrate<TelFailReturnNullV1, TelFailReturnNullV2>
    {
        public bool TryMigrateFrom(TelFailReturnNullV1 source, out TelFailReturnNullV2 result)
        {
            result = default!;
            return false;
        }
    }

    [JsonMigratable]
    public record class TelNoMigrateV1(string Name, int Age);
}
