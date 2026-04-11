using System.Diagnostics.Metrics;

namespace Egil.SystemTextJson.Migration.Migrations;

internal static class JsonMigrationMeter
{
    private static readonly Meter MeterInstance = new(JsonMigrationTelemetry.MeterName);

    private static readonly Counter<long> MigrationCounter = MeterInstance.CreateCounter<long>(
        JsonMigrationTelemetry.MigrationCounterName,
        unit: "{migration}",
        description: "Number of JSON migration attempts executed during deserialization.");

    private const string SourceTypeTag = "stjm.source_type";
    private const string TargetTypeTag = "stjm.target_type";
    private const string StatusTag = "stjm.migration.status";
    private const string StatusSuccess = "success";
    private const string StatusFailure = "failure";

    public static void RecordMigration(string sourceType, string targetType, bool success)
    {
        if (!MigrationCounter.Enabled)
        {
            return;
        }

        MigrationCounter.Add(
            1,
            new KeyValuePair<string, object?>(SourceTypeTag, sourceType),
            new KeyValuePair<string, object?>(TargetTypeTag, targetType),
            new KeyValuePair<string, object?>(StatusTag, success ? StatusSuccess : StatusFailure));
    }
}
