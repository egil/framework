using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Egil.Orleans.StateMigration;

internal static class StateMigrationTelemetry
{
    internal const string MeterName = "Egil.Orleans.StateMigration";
    internal const string ActivitySourceName = "Egil.Orleans.StateMigration";
    internal const string MigrationCounterName = "state_migration.type_migrations";
    internal const string MigrationActivityName = "state_migration.migrate";
    internal const string SourceTypeTagName = "state.migration.source_type";
    internal const string TargetTypeTagName = "state.migration.target_type";
    internal const string MigrationKindTagName = "state.migration.kind";
    internal const string StaticMigrationKind = "static";
    internal const string ExternalMigrationKind = "external";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Counter<long> TypeMigrationCounter = Meter.CreateCounter<long>(
        MigrationCounterName,
        unit: "{migration}",
        description: "Number of successful state type migrations.");
    private static readonly ConcurrentDictionary<Type, string> TypeTags = new();

    public static Activity? StartMigrationActivity(Type sourceType, Type targetType, string migrationKind)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        Activity? activity = ActivitySource.StartActivity(MigrationActivityName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(SourceTypeTagName, GetTypeTag(sourceType));
        activity.SetTag(TargetTypeTagName, GetTypeTag(targetType));
        activity.SetTag(MigrationKindTagName, migrationKind);
        return activity;
    }

    public static void RecordSuccessfulMigration(Type sourceType, Type targetType, string migrationKind)
    {
        TagList tags = new()
        {
            { SourceTypeTagName, GetTypeTag(sourceType) },
            { TargetTypeTagName, GetTypeTag(targetType) },
            { MigrationKindTagName, migrationKind },
        };

        TypeMigrationCounter.Add(1, tags);
    }

    public static void SetActivitySuccess(Activity? activity)
        => activity?.SetStatus(ActivityStatusCode.Ok);

    public static void SetActivityFailure(Activity? activity, string description)
        => activity?.SetStatus(ActivityStatusCode.Error, description);

    public static void SetActivityFailure(Activity? activity, Exception exception)
        => activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

    private static string GetTypeTag(Type type)
        => TypeTags.GetOrAdd(type, static currentType => currentType.FullName ?? currentType.Name);
}
