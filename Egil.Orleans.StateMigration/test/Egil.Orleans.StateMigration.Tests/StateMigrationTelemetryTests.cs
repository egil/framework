using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateMigrationTelemetryTests
{
    private const string MeterName = "Egil.Orleans.StateMigration";
    private const string ActivitySourceName = "Egil.Orleans.StateMigration";
    private const string MigrationCounterName = "state_migration.type_migrations";
    private const string MigrationActivityName = "state_migration.migrate";
    private const string SourceTypeTagName = "state.migration.source_type";
    private const string TargetTypeTagName = "state.migration.target_type";
    private const string MigrationKindTagName = "state.migration.kind";

    private static readonly Type InvokerType =
        typeof(Storage<>).Assembly.GetType("Egil.Orleans.StateMigration.StateMigrationInvoker", throwOnError: true)!;

    private static readonly MethodInfo TryMigrateMethod =
        InvokerType.GetMethod("TryMigrate", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find StateMigrationInvoker.TryMigrate.");

    [Fact]
    public void State_migration_invoker_emits_counter_with_expected_tags()
    {
        long migrationCount = 0;
        using MeterListener listener = CreateMigrationCounterListener(
            typeof(TelemetryLegacyState),
            typeof(TelemetryCurrentState),
            "static",
            measurement => migrationCount += measurement);

        bool migrated = InvokeTryMigrate(
            new TelemetryLegacyState("alice"),
            typeof(TelemetryLegacyState),
            typeof(TelemetryCurrentState),
            out object? migratedState);

        Assert.True(migrated);
        Assert.IsType<TelemetryCurrentState>(migratedState);
        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public void State_migration_invoker_emits_activity_with_expected_tags()
    {
        Activity? migrationActivity = null;
        using ActivityListener listener = CreateMigrationActivityListener(activity =>
        {
            if (!TagsMatch(
                    activity,
                    typeof(TelemetryLegacyState),
                    typeof(TelemetryCurrentState),
                    "static"))
            {
                return;
            }

            migrationActivity = activity;
        });

        bool migrated = InvokeTryMigrate(
            new TelemetryLegacyState("alice"),
            typeof(TelemetryLegacyState),
            typeof(TelemetryCurrentState),
            out _);

        Assert.True(migrated);
        Assert.NotNull(migrationActivity);
        Assert.Equal(MigrationActivityName, migrationActivity.OperationName);
    }

    [Fact]
    public void Migration_resolver_emits_counter_for_external_migrations()
    {
        long migrationCount = 0;
        using MeterListener listener = CreateMigrationCounterListener(
            typeof(TelemetryResolverSourceState),
            typeof(TelemetryResolverTargetState),
            "external",
            measurement => migrationCount += measurement);

        var services = new ServiceCollection();
        services.AddSingleton<IMigrate<TelemetryResolverSourceState, TelemetryResolverTargetState>, TelemetryExternalMigrator>();
        services.AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        TelemetryResolverTargetState migrated =
            resolver.Migrate<TelemetryResolverSourceState, TelemetryResolverTargetState>(new("alice"));

        Assert.Equal("external:alice", migrated.Value);
        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public void Migration_resolver_emits_counter_for_static_migrations()
    {
        long migrationCount = 0;
        using MeterListener listener = CreateMigrationCounterListener(
            typeof(TelemetryResolverStaticSourceState),
            typeof(TelemetryResolverStaticTargetState),
            "static",
            measurement => migrationCount += measurement);

        var services = new ServiceCollection();
        services.AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        TelemetryResolverStaticTargetState migrated =
            resolver.Migrate<TelemetryResolverStaticSourceState, TelemetryResolverStaticTargetState>(new("alice"));

        Assert.Equal("static:alice", migrated.Value);
        Assert.Equal(1, migrationCount);
    }

    private static MeterListener CreateMigrationCounterListener(
        Type expectedSourceType,
        Type expectedTargetType,
        string expectedMigrationKind,
        Action<long> onMeasurement)
    {
        string expectedSourceTag = GetTypeTag(expectedSourceType);
        string expectedTargetTag = GetTypeTag(expectedTargetType);

        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (!string.Equals(instrument.Meter.Name, MeterName, StringComparison.Ordinal)
                || !string.Equals(instrument.Name, MigrationCounterName, StringComparison.Ordinal))
            {
                return;
            }

            currentListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            if (!TagsMatch(tags, expectedSourceTag, expectedTargetTag, expectedMigrationKind))
            {
                return;
            }

            onMeasurement(measurement);
        });
        listener.Start();
        return listener;
    }

    private static ActivityListener CreateMigrationActivityListener(Action<Activity> onActivityStopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = onActivityStopped,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static bool InvokeTryMigrate(object source, Type sourceType, Type targetType, out object? migrated)
    {
        object?[] arguments = [source, sourceType, targetType, null];
        bool migratedFlag = (bool)TryMigrateMethod.Invoke(null, arguments)!;
        migrated = arguments[3];
        return migratedFlag;
    }

    private static bool TagsMatch(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string expectedSourceType,
        string expectedTargetType,
        string expectedMigrationKind)
        => string.Equals(GetTagValue(tags, SourceTypeTagName), expectedSourceType, StringComparison.Ordinal)
           && string.Equals(GetTagValue(tags, TargetTypeTagName), expectedTargetType, StringComparison.Ordinal)
           && string.Equals(GetTagValue(tags, MigrationKindTagName), expectedMigrationKind, StringComparison.Ordinal);

    private static bool TagsMatch(
        Activity activity,
        Type expectedSourceType,
        Type expectedTargetType,
        string expectedMigrationKind)
        => string.Equals(GetActivityTagValue(activity, SourceTypeTagName), GetTypeTag(expectedSourceType), StringComparison.Ordinal)
           && string.Equals(GetActivityTagValue(activity, TargetTypeTagName), GetTypeTag(expectedTargetType), StringComparison.Ordinal)
           && string.Equals(GetActivityTagValue(activity, MigrationKindTagName), expectedMigrationKind, StringComparison.Ordinal);

    private static string? GetTagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private static string? GetActivityTagValue(Activity activity, string key)
    {
        foreach ((string? currentKey, object? value) in activity.TagObjects)
        {
            if (string.Equals(currentKey, key, StringComparison.Ordinal))
            {
                return value?.ToString();
            }
        }

        return null;
    }

    private static string GetTypeTag(Type type)
        => type.FullName ?? type.Name;

    private sealed record TelemetryLegacyState(string Name);

    private sealed record TelemetryCurrentState(string DisplayName)
        : IMigrateFrom<TelemetryLegacyState, TelemetryCurrentState>
    {
        public static TelemetryCurrentState From(TelemetryLegacyState source)
            => new($"migrated:{source.Name}");
    }

    private sealed record TelemetryResolverSourceState(string Value);

    private sealed record TelemetryResolverTargetState(string Value);

    private sealed class TelemetryExternalMigrator
        : IMigrate<TelemetryResolverSourceState, TelemetryResolverTargetState>
    {
        public TelemetryResolverTargetState Migrate(TelemetryResolverSourceState source)
            => new($"external:{source.Value}");
    }

    private sealed record TelemetryResolverStaticSourceState(string Value);

    private sealed record TelemetryResolverStaticTargetState(string Value)
        : IMigrateFrom<TelemetryResolverStaticSourceState, TelemetryResolverStaticTargetState>
    {
        public static TelemetryResolverStaticTargetState From(TelemetryResolverStaticSourceState source)
            => new($"static:{source.Value}");
    }
}
