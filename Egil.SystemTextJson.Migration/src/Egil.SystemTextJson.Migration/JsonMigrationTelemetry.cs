namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Provides well-known names for the OpenTelemetry-compatible metrics
/// emitted by the JSON migration library.
/// </summary>
/// <remarks>
/// Use these constants when configuring your metrics pipeline, for example:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(metrics => metrics.AddMeter(JsonMigrationTelemetry.MeterName));
/// </code>
/// </remarks>
public static class JsonMigrationTelemetry
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> used by this library.
    /// </summary>
    public const string MeterName = "Egil.SystemTextJson.Migration";

    /// <summary>
    /// The name of the counter that records completed migration attempts.
    /// </summary>
    public const string MigrationCounterName = "stjm.migrations";
}
