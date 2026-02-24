using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateMigrationServiceCollectionExtensionsTests
{
    [Fact]
    public void Add_state_migrator_registers_external_migrator_for_resolution()
    {
        var services = new ServiceCollection();
        services
            .AddStateMigrator<Phase5SourceState, Phase5TargetState, Phase5Migrator>()
            .AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        Phase5TargetState migrated = resolver.Migrate<Phase5SourceState, Phase5TargetState>(new("alice"));

        Assert.Equal("migrated:alice", migrated.Name);
    }

    [Fact]
    public void Add_state_migration_fails_for_open_generic_migrator_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IMigrate<,>), typeof(OpenGenericMigrator<,>));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddStateMigration());

        Assert.Contains("closed generic", exception.Message, StringComparison.Ordinal);
    }
}

public sealed record Phase5SourceState(string Name);

public sealed record Phase5TargetState(string Name);

public sealed class Phase5Migrator : IMigrate<Phase5SourceState, Phase5TargetState>
{
    public Phase5TargetState Migrate(Phase5SourceState source)
        => new($"migrated:{source.Name}");
}

public sealed class OpenGenericMigrator<TSource, TTarget> : IMigrate<TSource, TTarget>
{
    public TTarget Migrate(TSource source)
        => throw new NotSupportedException("Open generic migrator should never be executed.");
}
