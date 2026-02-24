using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class MigrationResolverTests
{
    [Fact]
    public void Uses_static_migration_when_target_implements_migrate_from()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMigrate<V1State, V2StateWithStaticMigration>, ExternalV1ToV2WithDifferentBehaviorMigrator>();
        services.AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        V2StateWithStaticMigration migrated = resolver.Migrate<V1State, V2StateWithStaticMigration>(new("from-v1"));

        Assert.Equal("static:from-v1", migrated.Value);
    }

    [Fact]
    public void Falls_back_to_external_migrator_when_static_migration_is_not_available()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMigrate<V1State, V2StateWithoutStaticMigration>, ExternalV1ToV2Migrator>();
        services.AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        V2StateWithoutStaticMigration migrated = resolver.Migrate<V1State, V2StateWithoutStaticMigration>(new("from-v1"));

        Assert.Equal("external:from-v1", migrated.Value);
    }

    [Fact]
    public void Throws_when_no_migration_path_exists()
    {
        var services = new ServiceCollection();
        services.AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => resolver.Migrate<V1State, V3StateWithoutMigration>(new("from-v1")));

        Assert.Contains(nameof(V1State), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(V3StateWithoutMigration), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Throws_when_duplicate_external_migrators_are_registered_for_same_pair()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMigrate<V1State, V2StateWithoutStaticMigration>, ExternalV1ToV2Migrator>();
        services.AddSingleton<IMigrate<V1State, V2StateWithoutStaticMigration>, DuplicateExternalV1ToV2Migrator>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddStateMigration());

        Assert.Contains(nameof(V1State), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(V2StateWithoutStaticMigration), exception.Message, StringComparison.Ordinal);
    }

    private sealed record V1State(string Value);

    private sealed record V2StateWithStaticMigration(string Value) : IMigrateFrom<V1State, V2StateWithStaticMigration>
    {
        public static V2StateWithStaticMigration From(V1State source)
            => new($"static:{source.Value}");
    }

    private sealed record V2StateWithoutStaticMigration(string Value);

    private sealed record V3StateWithoutMigration(string Value);

    private sealed class ExternalV1ToV2Migrator : IMigrate<V1State, V2StateWithoutStaticMigration>
    {
        public V2StateWithoutStaticMigration Migrate(V1State source)
            => new($"external:{source.Value}");
    }

    private sealed class ExternalV1ToV2WithDifferentBehaviorMigrator : IMigrate<V1State, V2StateWithStaticMigration>
    {
        public V2StateWithStaticMigration Migrate(V1State source)
            => new($"external:{source.Value}");
    }

    private sealed class DuplicateExternalV1ToV2Migrator : IMigrate<V1State, V2StateWithoutStaticMigration>
    {
        public V2StateWithoutStaticMigration Migrate(V1State source)
            => new($"duplicate:{source.Value}");
    }
}
