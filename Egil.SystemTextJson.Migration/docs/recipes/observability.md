# Observability / Telemetry

## Enabling the OTel migration counter

The library emits an OpenTelemetry-compatible counter (`stjm.migrations`) via `System.Diagnostics.Metrics`. Each migration attempt records the source type, target type, and status.

**Console / test app — subscribe with `MeterListener`:**

<!-- snippet: otel_meter_listener -->
<a id='snippet-otel_meter_listener'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/TelemetrySample.cs#L24-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-otel_meter_listener' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**ASP.NET Core — register the meter with OpenTelemetry:**

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(JsonMigrationTelemetry.MeterName);
        // Add your exporter: .AddPrometheusExporter(), .AddOtlpExporter(), etc.
    });
```

> **Note:** The meter name is available as `JsonMigrationTelemetry.MeterName` (`"Egil.SystemTextJson.Migration"`) and the counter name is `JsonMigrationTelemetry.MigrationCounterName` (`"stjm.migrations"`).

## Monitoring migration volume in production

The `stjm.migrations` counter includes three tags on each measurement:

| Tag | Description | Example |
|-----|-------------|---------|
| `stjm.source_type` | Full type name of the source (old) type | `MyApp.UserV1` |
| `stjm.target_type` | Full type name of the target (current) type | `MyApp.UserV2` |
| `stjm.migration.status` | `"success"` or `"failure"` | `success` |

**Dashboard / alerting ideas:**

- **Migration volume over time:** Track `stjm.migrations` grouped by `stjm.target_type`. A healthy system should see this trend toward zero as old payloads are read-migrate-write-backed.
- **Failure rate:** Alert when `stjm.migration.status == "failure"` exceeds a threshold. Failed migrations may indicate a bug in the migrator or unexpected data formats.
- **Per-type breakdown:** Use `stjm.source_type` to identify which old schema versions are still being encountered. This helps decide when it's safe to remove old migrators.

```
# Example PromQL queries:
# Total migrations per minute
rate(stjm_migrations_total[1m])

# Failed migrations grouped by target type
rate(stjm_migrations_total{stjm_migration_status="failure"}[5m])

# Migration volume by source type (to track legacy payload decay)
sum by (stjm_source_type) (rate(stjm_migrations_total[1h]))
```

> **Note:** The counter uses `System.Diagnostics.Metrics`, which is the standard .NET metrics API. Any metrics collection system that supports this API (OpenTelemetry, Application Insights, etc.) can subscribe to it.
