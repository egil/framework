using System.Reflection;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateMigrationInvokerTests
{
    private static readonly Type InvokerType =
        typeof(Storage<>).Assembly.GetType("Egil.Orleans.StateMigration.StateMigrationInvoker", throwOnError: true)!;

    private static readonly MethodInfo TryMigrateMethod =
        InvokerType.GetMethod("TryMigrate", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find StateMigrationInvoker.TryMigrate.");

    [Fact]
    public void Try_migrate_returns_source_when_source_and_target_types_match()
    {
        var source = new PlainState("alice");

        bool migrated = InvokeTryMigrate(source, typeof(PlainState), typeof(PlainState), out object? migratedState);

        Assert.True(migrated);
        Assert.Same(source, migratedState);
    }

    [Fact]
    public void Try_migrate_uses_static_migrate_from_when_available()
    {
        var source = new LegacyState("alice");

        bool migrated = InvokeTryMigrate(source, typeof(LegacyState), typeof(CurrentState), out object? migratedState);

        Assert.True(migrated);
        CurrentState current = Assert.IsType<CurrentState>(migratedState);
        Assert.Equal("migrated:alice", current.DisplayName);
    }

    [Fact]
    public void Try_migrate_returns_false_when_no_direct_static_migration_exists()
    {
        var source = new LegacyState("alice");

        bool migrated = InvokeTryMigrate(source, typeof(LegacyState), typeof(UnrelatedState), out object? migratedState);

        Assert.False(migrated);
        Assert.Null(migratedState);
    }

    [Fact]
    public void Try_migrate_returns_false_when_static_migration_returns_null()
    {
        var source = new LegacyState("alice");

        bool migrated = InvokeTryMigrate(source, typeof(LegacyState), typeof(NullReturningState), out object? migratedState);

        Assert.False(migrated);
        Assert.Null(migratedState);
    }

    [Fact]
    public void Try_migrate_throws_for_null_source()
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeTryMigrateRaw(null, typeof(LegacyState), typeof(CurrentState)));

        ArgumentNullException inner = Assert.IsType<ArgumentNullException>(exception.InnerException);
        Assert.Equal("source", inner.ParamName);
    }

    private static bool InvokeTryMigrate(object source, Type sourceType, Type targetType, out object? migrated)
    {
        (bool migratedFlag, object? migratedState) = InvokeTryMigrateRaw(source, sourceType, targetType);
        migrated = migratedState;
        return migratedFlag;
    }

    private static (bool migrated, object? migratedState) InvokeTryMigrateRaw(object? source, Type sourceType, Type targetType)
    {
        object?[] arguments = [source, sourceType, targetType, null];
        bool migrated = (bool)TryMigrateMethod.Invoke(null, arguments)!;
        return (migrated, arguments[3]);
    }

    private sealed record PlainState(string Name);

    private sealed record LegacyState(string Name);

    private sealed record CurrentState(string DisplayName) : IMigrateFrom<LegacyState, CurrentState>
    {
        public static CurrentState From(LegacyState source)
            => new($"migrated:{source.Name}");
    }

    private sealed record UnrelatedState(string DisplayName);

    private sealed record NullReturningState(string DisplayName) : IMigrateFrom<LegacyState, NullReturningState>
    {
        public static NullReturningState From(LegacyState source)
            => null!;
    }
}
